using System.Globalization;
using FluentValidation.Results;
using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Orders;

/// <summary>What the checks of R7–R13 worked out for one order: its terms and what it would reserve.</summary>
internal sealed record OrderQuote(
    Account Account,
    Instrument Instrument,
    OrderSide Side,
    decimal Quantity,
    string Currency,
    decimal ReferencePrice,
    DateOnly ReferencePriceDate,
    decimal PriceLimit,
    OrderFeeTerms Fees,
    string Connection,
    decimal EstimatedGross,
    decimal EstimatedFee,
    decimal MaximumGross,
    decimal MaximumFee,
    decimal AvailableCash,
    decimal? AvailableQuantity)
{
    /// <summary>R12: the reserved cash of a buy; null for a sell.</summary>
    public decimal? MaximumCost => Side == OrderSide.Buy ? MaximumGross + MaximumFee : null;
}

/// <summary>The services the checks read through.</summary>
internal sealed record OrderCheckServices(
    IApplicationDbContext Db,
    ICurrencyCatalog Currencies,
    IBrokerConnections Connections,
    IMarketData MarketData,
    IInstrumentPriceStore Prices);

/// <summary>
/// Orders R7–R13: the account, instrument, price and reservation checks of an order, shared by place and preview so
/// both answer with the same 400s. Place runs them under the Account lock.
/// </summary>
internal static class OrderChecks
{
    public const string NotCustodialMessage = "An order can be placed only on a Custodial account.";
    public const string NoBrokerConnectionMessage = "The firm has no broker connection, so orders cannot be placed.";
    public const string ClosedMessage = "Cannot post a transaction while the account is closed. Reopen the account first.";

    public static async Task<OrderQuote> RunAsync(
        OrderCheckServices services,
        int accountId,
        Guid instrumentPublicId,
        OrderSide side,
        decimal quantity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var db = services.Db;
        var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == accountId, cancellationToken);
        var customer = await db.Users.AsNoTracking().SingleAsync(row => row.Id == account.CustomerId, cancellationToken);

        // R7: a Custodial account that is Open, not closing, of a Customer who is not Disabled.
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            throw Error("AccountId", NotCustodialMessage);
        }

        if (account.Status == AccountStatus.Closed)
        {
            throw Error(string.Empty, ClosedMessage);
        }

        await AccountClosing.EnsureNotClosingAsync(db, account.Id, cancellationToken);
        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot place an order while the customer is disabled. Enable the customer first.");
        }

        // R7: the Tenant names a broker connection that is configured on this host.
        var connection = await db.Tenants.AsNoTracking()
            .Where(row => row.Id == account.TenantId)
            .Select(row => row.BrokerConnection)
            .SingleAsync(cancellationToken);
        if (connection is null || !services.Connections.IsConfigured(connection))
        {
            throw Error("AccountId", NoBrokerConnectionMessage);
        }

        // R8: an order is a hand trade; on an assigned account only a rebalance trades.
        await PortfolioTradeLock.RejectIfAssignedAsync(db, account.Id, TransactionType.Buy, cancellationToken);

        // R9: an active Instrument of the Tenant; a buy needs it quoted in the Account's currency.
        var instrument = await db.Instruments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PublicId == instrumentPublicId && row.TenantId == account.TenantId, cancellationToken);
        if (instrument is null)
        {
            throw Error("InstrumentId", "Instrument was not found.");
        }

        if (!instrument.IsActive)
        {
            throw Error("InstrumentId", "Instrument is disabled.");
        }

        var quote = instrument.QuoteCurrency.Trim().ToUpperInvariant();
        var accountCurrency = account.Currency.Trim().ToUpperInvariant();
        if (side == OrderSide.Buy && !string.Equals(quote, accountCurrency, StringComparison.Ordinal))
        {
            throw Error(
                "InstrumentId",
                $"{instrument.Symbol} is quoted in {quote} and this account is in {accountCurrency}. Convert cash to a {quote} Custodial account and buy there.");
        }

        // R10: the latest stored price, at most 4 days old.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var price = services.MarketData.TryGetPrice(instrument.Id, now);
        var priceDate = await services.Prices.LatestDateAsync(instrument.Id, today, cancellationToken);
        if (price is null || price.Amount <= 0m || priceDate is null)
        {
            throw Error("InstrumentId", $"No price is stored for {instrument.Symbol}.");
        }

        if (!OrderRules.IsReferencePriceFresh(priceDate.Value, today))
        {
            throw Error(
                "InstrumentId",
                $"The latest price of {instrument.Symbol} is from {priceDate.Value:yyyy-MM-dd}. An order needs a price from the last {OrderRules.ReferencePriceMaxAgeDays} days.");
        }

        // R11: the fee terms are those of the day the order is placed.
        var places = services.Currencies.TryGet(quote)?.DecimalPlaces ?? 2;
        var schedule = await ApplicableTradeFeeSchedule.ForAsync(db, account.TenantId, quote, today, cancellationToken);
        var fees = new OrderFeeTerms(schedule, places);

        var referencePrice = price.Amount;
        var priceLimit = OrderRules.PriceLimit(side, referencePrice);
        var estimatedGross = OrderRules.MaximumGross(quantity, referencePrice);
        var maximumGross = OrderRules.MaximumGross(quantity, priceLimit);
        var maximumFee = fees.FeeFor(maximumGross);
        var availableCash = await AccountCash.AvailableAsync(db, account.Id, cancellationToken);
        decimal? availableQuantity = null;

        if (side == OrderSide.Buy)
        {
            // R12: the most the order can cost must fit the available cash.
            var maximumCost = maximumGross + maximumFee;
            if (maximumCost > availableCash)
            {
                throw Error(
                    "Quantity",
                    $"This order can cost up to {Amount(maximumCost, places)} {quote} with fees. The account's available cash is {Amount(availableCash, places)} {quote}.");
            }
        }
        else
        {
            // R13: the quantity must fit the holding's available quantity; no holding is 0.
            availableQuantity = await OrderReservations.AvailableQuantityAsync(db, account.Id, instrument.Id, cancellationToken);
            if (quantity > availableQuantity)
            {
                throw Error("Quantity", $"The account has {Units(availableQuantity.Value)} {instrument.Symbol} available to sell.");
            }
        }

        return new OrderQuote(
            account, instrument, side, quantity, quote, referencePrice, priceDate.Value, priceLimit, fees, connection,
            estimatedGross, fees.FeeFor(estimatedGross), maximumGross, maximumFee, availableCash, availableQuantity);
    }

    public static string Amount(decimal amount, int places) => amount.ToString($"F{places}", CultureInfo.InvariantCulture);

    /// <summary>A quantity without trailing zeros: <c>40</c>, <c>0.5</c>.</summary>
    public static string Units(decimal quantity) => quantity.ToString("0.########", CultureInfo.InvariantCulture);

    private static ValidationException Error(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}

/// <summary>
/// Orders R40, in-kind-transfers R43, R44: the quantity an Account's live sell orders and open transfers out reserve in
/// an Instrument, the quantity of its buy fills that have not settled, and what is left. The one read of these
/// quantities, used by the sell order check, the adjustment, the transfer out and the holdings item.
/// </summary>
internal static class OrderReservations
{
    /// <summary>The reserved quantity of the Account's sell orders in the Instrument (an order that is not live reserves
    /// 0) plus the quantity of its open transfers out in it (in-kind-transfers R43).</summary>
    public static async Task<decimal> ReservedQuantityAsync(
        IApplicationDbContext db,
        int accountId,
        int instrumentId,
        CancellationToken cancellationToken)
    {
        var orders = await db.Orders.IgnoreQueryFilters()
            .Where(row => row.AccountId == accountId && row.InstrumentId == instrumentId && row.Side == OrderSide.Sell)
            .SumAsync(row => (decimal?)row.ReservedQuantity, cancellationToken) ?? 0m;
        return orders + await TransfersOutAsync(db, accountId, instrumentId, cancellationToken);
    }

    /// <summary>In-kind-transfers R43: the quantity of the Account's open transfers out in the Instrument.</summary>
    public static async Task<decimal> TransfersOutAsync(
        IApplicationDbContext db, int accountId, int instrumentId, CancellationToken cancellationToken) =>
        await db.InKindTransfers.IgnoreQueryFilters()
            .Where(row => row.AccountId == accountId && row.InstrumentId == instrumentId && row.Direction == TransferDirection.Out
                          && (row.Status == InKindTransferStatus.Requested || row.Status == InKindTransferStatus.Approved
                              || row.Status == InKindTransferStatus.Sent))
            .SumAsync(row => (decimal?)row.Quantity, cancellationToken) ?? 0m;

    /// <summary>In-kind-transfers R44: the quantity of the Account's posted buy fills in the Instrument that are not settled
    /// and not broken.</summary>
    public static async Task<decimal> UnsettledQuantityAsync(
        IApplicationDbContext db, int accountId, int instrumentId, CancellationToken cancellationToken) =>
        await UnsettledFills(db, accountId).Where(row => row.InstrumentId == instrumentId)
            .SumAsync(row => (decimal?)row.Quantity, cancellationToken) ?? 0m;

    /// <summary>In-kind-transfers R44: the posted buy fills of an Account that are not settled, with their Instrument.</summary>
    public static IQueryable<UnsettledFill> UnsettledFills(IApplicationDbContext db, int accountId) =>
        from fill in db.OrderFills.IgnoreQueryFilters()
        join order in db.Orders.IgnoreQueryFilters() on fill.OrderId equals order.Id
        where order.AccountId == accountId && order.Side == OrderSide.Buy
              && fill.Status == OrderFillStatus.Posted && fill.SettledAt == null
        select new UnsettledFill { InstrumentId = order.InstrumentId, Quantity = fill.Quantity };

    internal sealed class UnsettledFill
    {
        public int InstrumentId { get; init; }

        public decimal Quantity { get; init; }
    }

    /// <summary>Orders R14: true when the Account has a live order: an open one, or one with a fill that is not
    /// posted yet.</summary>
    public static Task<bool> HasLiveOrderAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken)
    {
        var open = Enum.GetValues<OrderStatus>().Where(Order.IsOpenStatus).ToArray();
        return db.Orders.IgnoreQueryFilters().AnyAsync(
            row => row.AccountId == accountId
                   && (open.Contains(row.Status)
                       || db.OrderFills.IgnoreQueryFilters().Any(fill => fill.OrderId == row.Id && fill.Status == OrderFillStatus.Pending)),
            cancellationToken);
    }

    /// <summary>The holding's quantity minus its reserved quantity; no holding is 0.</summary>
    public static async Task<decimal> AvailableQuantityAsync(
        IApplicationDbContext db,
        int accountId,
        int instrumentId,
        CancellationToken cancellationToken)
    {
        var quantity = await db.Holdings.IgnoreQueryFilters()
            .Where(row => row.AccountId == accountId && row.InstrumentId == instrumentId)
            .Select(row => (decimal?)row.Quantity)
            .SingleOrDefaultAsync(cancellationToken) ?? 0m;
        return AvailableQuantity.Of(quantity, await ReservedQuantityAsync(db, accountId, instrumentId, cancellationToken));
    }
}
