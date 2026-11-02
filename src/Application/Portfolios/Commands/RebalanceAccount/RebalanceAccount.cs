using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Rebalance;
using MyWealthV2.Domain.ValueObjects;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;

[Authorize(Policy = Policies.TransactionsCreate)]
public class RebalanceAccountCommand : IRequest<RebalanceResultDto>
{
    public Guid AccountId { get; init; }

    public Guid Handle { get; init; }
}

public class RebalanceAccountCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMarketData marketData,
    IFxRate fxRates,
    TimeProvider time)
    : IRequestHandler<RebalanceAccountCommand, RebalanceResultDto>
{
    public async Task<RebalanceResultDto> Handle(RebalanceAccountCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RebalanceResultDto? result = null;
            await db.ExecuteInTransactionAsync(async token =>
            {
                result = await RebalanceAsync(request, token);
            }, cancellationToken);
            return result!;
        }
        catch (RebalanceException exception)
        {
            db.ClearTrackedChanges();
            await RecordFailureAsync(request.AccountId, exception.Message, cancellationToken);
            throw;
        }
    }

    private async Task<RebalanceResultDto> RebalanceAsync(
        RebalanceAccountCommand request,
        CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
            .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
            ?? throw new NotFoundException("Account", request.AccountId);
        await db.LockAccountAsync(account.Id, cancellationToken);

        var assignment = await db.AccountPortfolioAssignments.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
            cancellationToken);
        if (assignment is null || account.Status != AccountStatus.Open
            || account.Type is not (AccountType.Brokerage or AccountType.Other))
        {
            throw new RebalanceException("account_not_eligible", "The account is not an open brokerage or other account with an active portfolio.");
        }

        var portfolio = await db.Portfolios.Include(row => row.Lines)
            .SingleOrDefaultAsync(row => row.Id == assignment.PortfolioId, cancellationToken)
            ?? throw new RebalanceException("portfolio_inactive", "The portfolio is not available.");
        if (!portfolio.IsActive)
        {
            throw new RebalanceException("portfolio_inactive", "The portfolio is disabled.");
        }

        var plan = await BuildPlanAsync(account, portfolio, cancellationToken);
        if (plan.Status == RebalanceStatus.Failed)
        {
            throw new RebalanceException(plan.Code!, plan.Message!);
        }

        var now = time.GetUtcNow();
        if (plan.Trades.Count > 0)
        {
            var cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
            foreach (var trade in plan.Trades)
            {
                var holding = await db.Holdings.SingleOrDefaultAsync(
                    row => row.AccountId == account.Id && row.InstrumentId == trade.InstrumentId,
                    cancellationToken);
                var price = Money.Create(trade.Price, trade.QuoteCurrency);
                var reference = $"rb:{request.Handle:N}:{(trade.Buy ? "B" : "S")}:{trade.InstrumentId}";
                Domain.Entities.Transaction transaction;
                try
                {
                    transaction = trade.Buy
                        ? Domain.Entities.Transaction.PostBuy(
                            account.TenantId, account.Id, account.Type, account.Currency, trade.InstrumentId,
                            trade.Quantity, price, trade.QuoteToAccount, now, "Rebalance", reference, cash)
                        : Domain.Entities.Transaction.PostSell(
                            account.TenantId, account.Id, account.Type, account.Currency, trade.InstrumentId,
                            trade.Quantity, price, trade.QuoteToAccount, now, "Rebalance", reference, cash,
                            holding?.Quantity ?? 0m, holding?.CostAmount ?? 0m);
                }
                catch (DomainException exception)
                {
                    throw new RebalanceException("insufficient_cash", exception.Message);
                }

                db.Transactions.Add(transaction);
                var key = Key(account.PublicId, request.Handle, trade.InstrumentId, trade.Buy);
                db.IdempotencyRecords.Add(IdempotencyRecord.Create(
                    account.TenantId,
                    key,
                    "POST",
                    $"/accounts/{account.PublicId}/rebalance",
                    TransactionRequestHash.Compute("POST", $"/accounts/{account.PublicId}/rebalance", reference),
                    200,
                    JsonSerializer.Serialize(new { id = transaction.PublicId }),
                    now));
                await db.SaveChangesAsync(cancellationToken);
                await HoldingProjection.SyncAsync(db, account.TenantId, account.Id, [trade.InstrumentId], cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
            }
        }

        assignment.RecordRebalance(plan.Status, null, now);
        await db.SaveChangesAsync(cancellationToken);
        return new RebalanceResultDto(plan.Status.ToString());
    }

    private async Task<RebalancePlan> BuildPlanAsync(
        Account account,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        var holdingRows = await (
            from holding in db.Holdings
            join instrument in db.Instruments on holding.InstrumentId equals instrument.Id
            where holding.AccountId == account.Id && holding.Quantity > 0m
            select new { holding, instrument }).ToListAsync(cancellationToken);
        var lineInstruments = await db.Instruments
            .Where(instrument => portfolio.Lines.Select(line => line.InstrumentId).Contains(instrument.Id))
            .ToListAsync(cancellationToken);

        var holdings = new List<PlannerHolding>();
        foreach (var row in holdingRows)
        {
            holdings.Add(await QuoteAsync(
                row.instrument, row.holding.Quantity, account.Currency, portfolio.BaseCurrency, cancellationToken));
        }

        var targets = new List<PlannerTarget>();
        foreach (var line in portfolio.Lines.OrderBy(line => line.SortOrder))
        {
            var instrument = lineInstruments.Single(row => row.Id == line.InstrumentId);
            var quoted = await QuoteAsync(instrument, 0m, account.Currency, portfolio.BaseCurrency, cancellationToken);
            targets.Add(new PlannerTarget(
                quoted.InstrumentId, quoted.Symbol, quoted.QuoteCurrency, line.TargetWeight,
                quoted.Price, quoted.QuoteToBase, quoted.QuoteToAccount));
        }

        decimal? accountToBase = await RateAsync(account.Currency, portfolio.BaseCurrency, cancellationToken);
        var cash = await TransactionCash.SumAsync(db, account.Id, cancellationToken);
        return RebalancePlanner.Plan(new RebalanceInput(
            portfolio.BaseCurrency,
            account.Currency,
            cash,
            accountToBase,
            portfolio.DriftAbsolute,
            portfolio.DriftRelative,
            portfolio.MinimumTrade,
            portfolio.CashResidualPercent,
            holdings,
            targets));
    }

    private async Task<PlannerHolding> QuoteAsync(
        Instrument instrument,
        decimal quantity,
        string accountCurrency,
        string baseCurrency,
        CancellationToken cancellationToken)
    {
        var price = marketData.TryGetPrice(instrument.Id, null);
        var quote = instrument.QuoteCurrency.Trim().ToUpperInvariant();
        decimal? amount = price is null || price.Amount <= 0m
            || !string.Equals(price.Currency, quote, StringComparison.OrdinalIgnoreCase)
            ? null
            : price.Amount;
        return new PlannerHolding(
            instrument.Id,
            instrument.Symbol,
            quote,
            quantity,
            amount,
            await RateAsync(quote, baseCurrency, cancellationToken),
            await RateAsync(quote, accountCurrency, cancellationToken));
    }

    private async Task<decimal?> RateAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return await fxRates.GetRateAsync(from, to, null, cancellationToken);
        }
        catch (FxRateNotFoundException)
        {
            return null;
        }
    }

    private async Task RecordFailureAsync(Guid accountId, string message, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(row => row.PublicId == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var assignment = await db.AccountPortfolioAssignments.SingleOrDefaultAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active,
            cancellationToken);
        if (assignment is null)
        {
            return;
        }

        assignment.RecordRebalance(RebalanceStatus.Failed, message, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid Key(Guid accountId, Guid handle, int instrumentId, bool buy)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}|{handle:N}|{instrumentId}|{buy}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
