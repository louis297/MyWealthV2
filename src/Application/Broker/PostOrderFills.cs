using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Orders;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Broker;

/// <summary>What the last step of a run did: fills posted, fills that still wait with a hold reason, and fills whose
/// posting failed.</summary>
public sealed record FillPostingOutcome(int Posted, int Waiting, int Failed);

/// <summary>
/// The one place a fill is posted (docs/features/orders.md R31–R36). A fill is posted as one <c>Buy</c> or
/// <c>Sell</c> with source <c>OrderFill</c>, at the broker's price, with the fee of R33 and its execution detail. A
/// fill that cannot be posted waits as <c>Pending</c> with a hold reason and is tried again by every run.
/// </summary>
public sealed class PostOrderFills(
    IApplicationDbContext db,
    IBrokerConnections connections,
    ICurrencyCatalog currencies,
    IFxRate fxRates,
    TimeProvider time,
    ILogger<PostOrderFills> logger)
{
    /// <summary>R36: tries every <c>Pending</c> fill of the connection again, oldest first, each in its own database
    /// transaction. A failure on one is logged with the order's PublicId and leaves it for the next run.</summary>
    public async Task<FillPostingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; no fill is posted.", connection);
            return new FillPostingOutcome(0, 0, 0);
        }

        db.ClearTrackedChanges();
        var pending = await (
                from fill in db.OrderFills.IgnoreQueryFilters().AsNoTracking()
                join order in db.Orders.IgnoreQueryFilters().AsNoTracking() on fill.OrderId equals order.Id
                where order.Connection == connection && fill.Status == OrderFillStatus.Pending
                orderby fill.Id
                select new { fill.Id, OrderId = order.Id, order.AccountId, order.PublicId })
            .ToListAsync(cancellationToken);

        var (posted, waiting, failed) = (0, 0, 0);
        foreach (var item in pending)
        {
            db.ClearTrackedChanges();
            try
            {
                bool? result = null;
                await db.ExecuteInTransactionAsync(async token =>
                {
                    result = null;
                    // R31: the Account row is locked as on every posting, and the fill is read again.
                    await db.LockAccountAsync(item.AccountId, token);
                    var order = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills)
                        .SingleAsync(row => row.Id == item.OrderId, token);
                    var fill = order.Fills.Single(row => row.Id == item.Id);
                    if (fill.Status != OrderFillStatus.Pending)
                    {
                        return;
                    }

                    var fees = await OrderFees.OfAsync(db, currencies, order, token);
                    result = await PostAsync(order, fill, fees, token);
                    await db.SaveChangesAsync(token);
                }, cancellationToken);
                if (result == true)
                {
                    posted++;
                }
                else if (result == false)
                {
                    waiting++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogError(exception, "A fill of order {Order} could not be posted; the next run tries again.", item.PublicId);
            }
        }

        return new FillPostingOutcome(posted, waiting, failed);
    }

    /// <summary>
    /// R31–R36: posts one <c>Pending</c> fill of <paramref name="order"/>, or holds it with the reason it cannot be
    /// posted. The caller holds the Account lock, runs this in its database transaction and saves afterwards. True
    /// when the fill is posted.
    /// </summary>
    public async Task<bool> PostAsync(Order order, OrderFill fill, OrderFeeTerms fees, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == order.AccountId, cancellationToken);
        if (account.Status == AccountStatus.Closed)
        {
            fill.Hold(FillHoldReason.AccountClosed);
            return false;
        }

        var now = time.GetUtcNow();
        var quote = order.Currency.Trim();
        var accountCurrency = account.Currency.Trim().ToUpperInvariant();
        var holding = await db.Holdings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == account.Id && row.InstrumentId == order.InstrumentId, cancellationToken);
        if (order.Side == OrderSide.Sell && (holding is null || fill.Quantity > holding.Quantity))
        {
            fill.Hold(FillHoldReason.InsufficientHolding);
            return false;
        }

        // R34: a sell in another currency is converted at the reference rate of the day it is booked.
        var fxRate = 1m;
        if (!string.Equals(quote, accountCurrency, StringComparison.Ordinal))
        {
            try
            {
                fxRate = await fxRates.GetRateAsync(quote, accountCurrency, now, cancellationToken);
            }
            catch (FxRateNotFoundException)
            {
                fill.Hold(FillHoldReason.NoFxRate);
                return false;
            }
        }

        // R32, R33: the gross at the broker's price, and the fee of the order across its fills.
        var gross = OrderRules.MaximumGross(fill.Quantity, fill.Price);
        var fee = order.FeeOfFill(gross, fees);
        var feeLeg = Round(fee * fxRate);
        var cash = order.Side == OrderSide.Buy
            ? -Round(gross * fxRate) - feeLeg
            : Round(fill.Quantity * fill.Price * fxRate) - feeLeg;

        // R36: the cash after the fill must cover what stays reserved: the other reservations of the Account, and
        // what this order still reserves once the fill is posted (R39).
        var cashSum = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        var reservedByOthers = await AccountCash.ReservedExceptOrderAsync(db, account.Id, order.Id, cancellationToken);
        var reservedAfter = reservedByOthers + order.ReservedCashAfterPosting(fill, gross, fee, fees);
        if (!AvailableCash.Allows(cashSum + cash, reservedAfter))
        {
            fill.Hold(FillHoldReason.InsufficientCash);
            return false;
        }

        var source = TransactionSource.OrderFill(order.PublicId);
        var execution = new TradeExecution(fill.PublicId, fill.TradeDate, fill.SettlementDate, fill.BrokerReference);
        var price = Money.Create(fill.Price, quote);
        var enteredFee = fees.Schedule is null ? (decimal?)null : fee;
        var transaction = order.Side == OrderSide.Buy
            ? Transaction.PostBuy(
                order.TenantId, account.Id, account.Type, accountCurrency, order.InstrumentId, fill.Quantity, price, fxRate, now,
                Transaction.BuyOrderMemo, null, source, cashSum, fees.Schedule, fees.DecimalPlaces, enteredFee, null, execution)
            : Transaction.PostSell(
                order.TenantId, account.Id, account.Type, accountCurrency, order.InstrumentId, fill.Quantity, price, fxRate, now,
                Transaction.SellOrderMemo, null, source, cashSum, holding!.Quantity, holding.CostAmount, fees.Schedule,
                fees.DecimalPlaces, enteredFee, null, execution);
        transaction.CreatedBy = SystemActor.Name;
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);
        transaction.SecurityLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);
        await HoldingProjection.SyncAsync(
            db, order.TenantId, account.Id, [order.InstrumentId], cancellationToken, SystemActor.Name);

        fill.MarkPosted(transaction.Id, transaction.CashLegs.Sum(leg => leg.Amount), now);
        order.RecordPostedFill(gross, fee, fees);
        return true;
    }

    /// <summary>
    /// Trade-settlement R28, R29, R32: the broker took <paramref name="fill"/> back. A fill that was never posted is
    /// <c>NotPosted</c> and nothing is written; a posted one is reversed by the flow, with the order's source, and the
    /// holdings follow. Runs in the report's own database transaction, under the Account lock.
    /// </summary>
    public async Task BreakAsync(Order order, OrderFill fill, ExecutionReport report, OrderFeeTerms fees, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (fill.Status == OrderFillStatus.Pending)
        {
            order.BreakFill(fill, report.Id, report.Reason, now, FillBreakOutcome.NotPosted, fees);
            return;
        }

        var original = await db.Transactions.IgnoreQueryFilters()
            .Include(row => row.FxDetail)
            .Include(row => row.FeeDetails)
            .Include(row => row.ExecutionDetail)
            .SingleAsync(row => row.Id == fill.TransactionId, cancellationToken);
        await db.LoadCashLegsAsync(original, cancellationToken);
        await db.LoadSecurityLegsAsync(original, cancellationToken);
        var cashSum = await TransactionCash.SumAsync(db, order.AccountId, cancellationToken);

        // Trade-settlement R30: decided once, when the report is processed; the first reason that applies.
        var holdReason = await BreakHoldReasonAsync(order, original, cashSum, cancellationToken);
        if (holdReason is not null)
        {
            order.BreakFill(fill, report.Id, report.Reason, now, FillBreakOutcome.NotApplied, fees, holdReason: holdReason);
            return;
        }

        var holding = await db.Holdings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == order.AccountId && row.InstrumentId == order.InstrumentId, cancellationToken);
        var reversal = original.Reverse(
            TransactionSource.OrderFill(order.PublicId), now,
            new Dictionary<int, decimal> { [order.AccountId] = cashSum },
            new Dictionary<(int AccountId, int InstrumentId), decimal> { [(order.AccountId, order.InstrumentId)] = holding?.Quantity ?? 0m });
        reversal.CreatedBy = SystemActor.Name;
        reversal.CashLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);
        reversal.SecurityLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);
        db.Transactions.Add(reversal);
        await db.SaveChangesAsync(cancellationToken);
        await HoldingProjection.SyncAsync(
            db, order.TenantId, order.AccountId, [order.InstrumentId], cancellationToken, SystemActor.Name);

        order.BreakFill(
            fill, report.Id, report.Reason, now, FillBreakOutcome.Reversed, fees,
            original.FeeDetails.Sum(detail => detail.FeeAmount), reversal.Id);
    }

    /// <summary>Trade-settlement R30: why the fill's Transaction cannot be reversed now, or <c>null</c>.</summary>
    private async Task<FillBreakHoldReason?> BreakHoldReasonAsync(
        Order order,
        Transaction original,
        decimal cashSum,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == order.AccountId, cancellationToken);
        if (account.Status == AccountStatus.Closed)
        {
            return FillBreakHoldReason.AccountClosed;
        }

        // ledger-write-guards Decision 1: only the latest live security activity on a holding is reversed.
        var later = await (
                from leg in db.TransactionSecurityLegs.IgnoreQueryFilters().AsNoTracking()
                join transaction in db.Transactions.IgnoreQueryFilters().AsNoTracking() on leg.TransactionId equals transaction.Id
                where leg.AccountId == order.AccountId
                      && leg.InstrumentId == order.InstrumentId
                      && transaction.Id > original.Id
                      && transaction.Type != TransactionType.Reversal
                      && transaction.ReversedByTransactionId == null
                select leg.TransactionId)
            .AnyAsync(cancellationToken);
        if (later)
        {
            return FillBreakHoldReason.LaterActivity;
        }

        // Withdrawals R36: a broken sell takes its proceeds back; available cash must not fall below 0.
        if (order.Side == OrderSide.Sell)
        {
            var reversalCash = -original.CashLegs.Sum(leg => leg.Amount);
            var reserved = await AccountCash.ReservedExceptOrderAsync(db, order.AccountId, order.Id, cancellationToken);
            if (!AvailableCash.Allows(cashSum + reversalCash, reserved))
            {
                return FillBreakHoldReason.InsufficientCash;
            }
        }

        return null;
    }

    private static decimal Round(decimal amount) => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
