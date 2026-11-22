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
    Settled = 3
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
        DateOnly day) => new()
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
        AcceptedDay = day
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
        BrokerRejectReason reason) => new()
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
        RejectReason = reason
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
}

/// <summary>One report of the broker's feed (orders R63). Its <see cref="Id"/> is its place in the feed.</summary>
public sealed class BrokerReport
{
    private BrokerReport()
    {
    }

    public long Id { get; private set; }

    public BrokerReportKind Kind { get; private set; }

    public int OrderId { get; private set; }

    public BrokerOrder Order { get; private set; } = null!;

    public int? FillId { get; private set; }

    public BrokerFill? Fill { get; private set; }

    public DateOnly? SettledDate { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>R57: the report of a fill.</summary>
    public static BrokerReport OfFill(BrokerOrder order, BrokerFill fill) =>
        new() { Kind = BrokerReportKind.Fill, Order = order, Fill = fill };
}
