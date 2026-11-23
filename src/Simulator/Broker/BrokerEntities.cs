using System.Globalization;

namespace MyWealthV2.Simulator.Broker;

/// <summary>How the broker answers new orders (orders R55).</summary>
public enum BrokerOrderMode
{
    Fill = 0,
    Hold = 1,
    Reject = 2
}

public enum BrokerOrderSide
{
    Buy = 0,
    Sell = 1
}

/// <summary>The broker's status of an order (orders R64).</summary>
public enum BrokerOrderStatus
{
    Working = 0,
    Filled = 1,
    Cancelled = 2,
    Expired = 3,
    Rejected = 4
}

/// <summary>Why the broker rejects an order (orders R21): the names the platform's adapter reads.</summary>
public enum BrokerRejectReason
{
    UnknownInstrument = 0,
    InvalidOrder = 1,
    MarketClosed = 2,
    Refused = 3
}

/// <summary>The kind of a report in the broker's feed (orders R63).</summary>
public enum BrokerReportKind
{
    Fill = 0,
    Cancelled = 1,
    Expired = 2,
    Settled = 3,

    /// <summary>Trade-settlement R40: the broker took a fill back.</summary>
    Broken = 4,

    /// <summary>Trade-settlement R36: one broker settlement is closed.</summary>
    Settlement = 5
}

/// <summary>Trade-settlement R38: whether the broker pays its settlement receipts at once or holds them.</summary>
public enum BrokerSettlementMode
{
    Pay = 0,
    Hold = 1
}

/// <summary>The one row of broker settings (orders R55, R56, R61): how new orders are answered, the slippage of a
/// fill price and the weekdays from trade to settlement.</summary>
public sealed class BrokerSettings
{
    public const int SingletonId = 1;

    private BrokerSettings()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public BrokerOrderMode Mode { get; private set; }

    /// <summary>The part of a new order that mode <c>Fill</c> fills at once, 1–100.</summary>
    public int FillPercent { get; private set; }

    /// <summary>The reason of mode <c>Reject</c>; null in the other modes.</summary>
    public BrokerRejectReason? RejectReason { get; private set; }

    public decimal SlippagePercent { get; private set; }

    public int SettlementDays { get; private set; }

    /// <summary>Trade-settlement R38.</summary>
    public BrokerSettlementMode SettlementMode { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R55: the mode, with the fill percent of <c>Fill</c> and the reason of <c>Reject</c>.</summary>
    public void SetMode(BrokerOrderMode mode, int fillPercent, BrokerRejectReason? rejectReason)
    {
        Mode = mode;
        FillPercent = fillPercent;
        RejectReason = mode == BrokerOrderMode.Reject ? rejectReason : null;
    }

    /// <summary>R56: applies to later fills.</summary>
    public void SetSlippage(decimal slippagePercent) => SlippagePercent = slippagePercent;

    /// <summary>R61: applies to later fills.</summary>
    public void SetSettlementDays(int settlementDays) => SettlementDays = settlementDays;

    /// <summary>Trade-settlement R38: applies to later settlements.</summary>
    public void SetSettlementMode(BrokerSettlementMode mode) => SettlementMode = mode;
}

/// <summary>An order in the simulator broker's books (orders R52–R54). The broker does not know which Tenant,
/// Account or Customer it is for (simulator R33).</summary>
public sealed class BrokerOrder
{
    private BrokerOrder()
    {
    }

    public int Id { get; private set; }

    /// <summary>R54: the <c>n</c> of the broker order id, counting the orders of the world from 1.</summary>
    public int Number { get; private set; }

    public string ClientOrderId { get; private set; } = "";

    /// <summary>R54: the hash of the request body, to tell a repeat from another order with the same id.</summary>
    public byte[] BodyHash { get; private set; } = [];

    public string Symbol { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public BrokerOrderSide Side { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal PriceLimit { get; private set; }

    public decimal ReferencePrice { get; private set; }

    public BrokerOrderStatus Status { get; private set; }

    public BrokerRejectReason? RejectReason { get; private set; }

    public decimal FilledQuantity { get; private set; }

    /// <summary>R60: the simulator day the order was accepted on; null for a rejected order.</summary>
    public DateOnly? AcceptedDay { get; private set; }

    /// <summary>Trade-settlement R35: the trust account number the order settles against, as the platform sent it.</summary>
    public string? SettlementAccount { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public List<BrokerFill> Fills { get; private set; } = [];

    /// <summary>The quantity not filled yet.</summary>
    public decimal Rest => Quantity - FilledQuantity;

    /// <summary>R54: <c>{WorldId:N}-O{n}</c>.</summary>
    public static string BrokerOrderId(Guid worldId, int number) =>
        $"{worldId:N}-O{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>R54: the number of a broker order id of <paramref name="worldId"/>, or false.</summary>
    public static bool TryReadNumber(string id, Guid worldId, out int number)
    {
        number = 0;
        var prefix = $"{worldId:N}-O";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>R54: an accepted order, working from <paramref name="day"/>.</summary>
    public static BrokerOrder Accept(
        int number,
        string clientOrderId,
        byte[] bodyHash,
        string symbol,
        string currency,
        BrokerOrderSide side,
        decimal quantity,
        decimal priceLimit,
        decimal referencePrice,
        DateOnly day,
        string? settlementAccount = null) => new()
    {
        Number = number,
        ClientOrderId = clientOrderId,
        BodyHash = bodyHash,
        Symbol = symbol,
        Currency = currency,
        Side = side,
        Quantity = quantity,
        PriceLimit = priceLimit,
        ReferencePrice = referencePrice,
        Status = BrokerOrderStatus.Working,
        AcceptedDay = day,
        SettlementAccount = settlementAccount
    };

    /// <summary>R54: a rejected order, kept so a repeat of the request gets the same answer.</summary>
    public static BrokerOrder Reject(
        int number,
        string clientOrderId,
        byte[] bodyHash,
        string symbol,
        string currency,
        BrokerOrderSide side,
        decimal quantity,
        decimal priceLimit,
        decimal referencePrice,
        BrokerRejectReason reason,
        string? settlementAccount = null) => new()
    {
        Number = number,
        ClientOrderId = clientOrderId,
        BodyHash = bodyHash,
        Symbol = symbol,
        Currency = currency,
        Side = side,
        Quantity = quantity,
        PriceLimit = priceLimit,
        ReferencePrice = referencePrice,
        Status = BrokerOrderStatus.Rejected,
        RejectReason = reason,
        SettlementAccount = settlementAccount
    };

    /// <summary>R56: a price above the limit of a buy, or below the limit of a sell, fills nothing.</summary>
    public bool AllowsPrice(decimal price) => Side == BrokerOrderSide.Buy ? price <= PriceLimit : price >= PriceLimit;

    /// <summary>R57: one fill of a working order, within its rest. The order is <c>Filled</c> when nothing is left.</summary>
    public BrokerFill Fill(int number, decimal quantity, decimal price, DateOnly tradeDate, DateOnly settlementDate)
    {
        if (Status != BrokerOrderStatus.Working)
        {
            throw new InvalidOperationException($"Only a working order can be filled; this one is {Status}.");
        }

        if (quantity <= 0 || quantity > Rest)
        {
            throw new InvalidOperationException("A fill must be greater than 0 and within the rest of the order.");
        }

        var fill = new BrokerFill(number, quantity, price, tradeDate, settlementDate);
        Fills.Add(fill);
        FilledQuantity += quantity;
        if (FilledQuantity == Quantity)
        {
            Status = BrokerOrderStatus.Filled;
        }

        return fill;
    }

    /// <summary>R58, R59, R62: a working order is cancelled; what was filled stays filled.</summary>
    public void Cancel() => End(BrokerOrderStatus.Cancelled);

    /// <summary>R60: a working order of an earlier simulator day expires.</summary>
    public void Expire() => End(BrokerOrderStatus.Expired);

    private void End(BrokerOrderStatus status)
    {
        if (Status != BrokerOrderStatus.Working)
        {
            throw new InvalidOperationException($"Only a working order can end as {status}; this one is {Status}.");
        }

        Status = status;
    }
}

/// <summary>One fill of an order (orders R57).</summary>
public sealed class BrokerFill
{
    private BrokerFill()
    {
    }

    internal BrokerFill(int number, decimal quantity, decimal price, DateOnly tradeDate, DateOnly settlementDate)
    {
        Number = number;
        Quantity = quantity;
        Price = price;
        TradeDate = tradeDate;
        SettlementDate = settlementDate;
        ContractNote = $"CN-{number.ToString(CultureInfo.InvariantCulture)}";
    }

    public int Id { get; private set; }

    /// <summary>R56, R57: counts the fills of the world from 1.</summary>
    public int Number { get; private set; }

    public int OrderId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly TradeDate { get; private set; }

    public DateOnly SettlementDate { get; private set; }

    public string ContractNote { get; private set; } = "";

    public bool Settled { get; private set; }

    /// <summary>Trade-settlement R40: the broker took the fill back; it never settles.</summary>
    public bool Broken { get; private set; }

    /// <summary>Trade-settlement R36: the broker settlement the fill settled in, when its order named a settlement
    /// account.</summary>
    public int? SettlementId { get; private set; }

    /// <summary>R61: the simulator day reached the settlement date; trade-settlement R36: in its settlement, if
    /// any.</summary>
    public void Settle(int? settlementId = null)
    {
        Settled = true;
        SettlementId = settlementId;
    }
}

/// <summary>
/// One broker settlement (trade-settlement R36): the fills of orders with a settlement account, grouped by settlement
/// account, currency and settlement date, with their net seen from the trust account (above 0 the broker pays).
/// </summary>
public sealed class BrokerSettlement
{
    private BrokerSettlement()
    {
    }

    public int Id { get; private set; }

    /// <summary>The <c>n</c> of the settlement id, counting the settlements of the world from 1.</summary>
    public int Number { get; private set; }

    public string SettlementAccount { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public DateOnly SettlementDate { get; private set; }

    public decimal NetAmount { get; private set; }

    public int FillCount { get; private set; }

    /// <summary>Trade-settlement R37–R39: what the broker paid of a net above 0.</summary>
    public decimal PaidAmount { get; private set; }

    /// <summary>R36: the first 8 hexadecimal digits of the world id, <c>-S</c> and <c>n</c>: it fits a bank reference
    /// of 18 characters.</summary>
    public static string SettlementId(Guid worldId, int number) =>
        $"{worldId.ToString("N")[..8]}-S{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The number of a settlement id of <paramref name="worldId"/>, or false.</summary>
    public static bool TryReadNumber(string id, Guid worldId, out int number)
    {
        number = 0;
        var prefix = $"{worldId.ToString("N")[..8]}-S";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    public static BrokerSettlement Open(int number, string settlementAccount, string currency, DateOnly settlementDate) => new()
    {
        Number = number,
        SettlementAccount = settlementAccount,
        Currency = currency,
        SettlementDate = settlementDate
    };

    /// <summary>R36: one fill more, <c>Round(quantity × price, 4)</c>, plus for a sell and minus for a buy.</summary>
    public void Add(BrokerOrder order, BrokerFill fill)
    {
        var gross = Math.Round(fill.Quantity * fill.Price, 4, MidpointRounding.AwayFromZero);
        NetAmount += order.Side == BrokerOrderSide.Sell ? gross : -gross;
        FillCount++;
    }

    /// <summary>R37, R38: the broker paid <paramref name="amount"/> of the settlement.</summary>
    public void RecordPaid(decimal amount) => PaidAmount += amount;
}

/// <summary>One report of the broker's feed (orders R63). Its <see cref="Id"/> is its place in the feed.</summary>
public sealed class BrokerReport
{
    private BrokerReport()
    {
    }

    public long Id { get; private set; }

    public BrokerReportKind Kind { get; private set; }

    /// <summary>The order the report is about; none on a <c>Settlement</c> report.</summary>
    public int? OrderId { get; private set; }

    public BrokerOrder? Order { get; private set; }

    public int? FillId { get; private set; }

    /// <summary>Trade-settlement R36: the settlement a <c>Settled</c> report names, or the one a <c>Settlement</c>
    /// report closes.</summary>
    public int? SettlementId { get; private set; }

    public BrokerSettlement? Settlement { get; private set; }

    public BrokerFill? Fill { get; private set; }

    public DateOnly? SettledDate { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>R57: the report of a fill.</summary>
    public static BrokerReport OfFill(BrokerOrder order, BrokerFill fill) =>
        new() { Kind = BrokerReportKind.Fill, Order = order, Fill = fill };

    /// <summary>R58, R59, R62: the report of a cancellation, with who or what cancelled.</summary>
    public static BrokerReport OfCancel(BrokerOrder order, string reason) =>
        new() { Kind = BrokerReportKind.Cancelled, Order = order, Reason = reason };

    /// <summary>R60: the report of an expiry.</summary>
    public static BrokerReport OfExpiry(BrokerOrder order) => new() { Kind = BrokerReportKind.Expired, Order = order };

    /// <summary>R61: the report of a settlement, on the fill's settlement date; trade-settlement R36: naming its
    /// broker settlement, if any.</summary>
    public static BrokerReport OfSettlement(BrokerOrder order, BrokerFill fill, BrokerSettlement? settlement = null) =>
        new() { Kind = BrokerReportKind.Settled, Order = order, Fill = fill, SettledDate = fill.SettlementDate, Settlement = settlement };

    /// <summary>Trade-settlement R36: the report that closes a broker settlement; it names no order.</summary>
    public static BrokerReport OfSettlementClosed(BrokerSettlement settlement) =>
        new() { Kind = BrokerReportKind.Settlement, Settlement = settlement };

    /// <summary>Trade-settlement R40: the report of a broken fill, with its reason.</summary>
    public static BrokerReport OfBreak(BrokerOrder order, BrokerFill fill, string reason) =>
        new() { Kind = BrokerReportKind.Broken, Order = order, Fill = fill, Reason = reason };
}
