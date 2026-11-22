using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// An instruction to buy or sell a quantity of one Instrument for one Custodial account at the Tenant's broker
/// (docs/features/orders.md R5, §5). Not a Transaction: each of its fills is posted as one. A live order reserves the
/// most it can still cost (a buy) or its quantity (a sell).
/// </summary>
public class Order : BaseAuditableEntity
{
    public const int MemoMaxLength = 200;
    public const int NoteMaxLength = 200;
    public const int QuantityDecimals = 8;

    private Order()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int InstrumentId { get; private set; }

    public OrderSide Side { get; private set; }

    public OrderType Type { get; private set; }

    public TimeInForce TimeInForce { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>The Instrument's quote currency when the order was placed.</summary>
    public string Currency { get; private set; } = string.Empty;

    public decimal ReferencePrice { get; private set; }

    public DateOnly ReferencePriceDate { get; private set; }

    public decimal PriceLimit { get; private set; }

    /// <summary>The schedule of R11, or null.</summary>
    public int? TradeFeeScheduleId { get; private set; }

    /// <summary>The Tenant's broker connection when the order was placed (R3).</summary>
    public string Connection { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public decimal FilledQuantity { get; private set; }

    /// <summary>The gross of the posted fills, in the quote currency.</summary>
    public decimal PostedGross { get; private set; }

    /// <summary>The fee charged on the posted fills, in the quote currency.</summary>
    public decimal FeeCharged { get; private set; }

    /// <summary>What the order reserves now (R39); 0 on a sell and when it is not live.</summary>
    public decimal ReservedCash { get; private set; }

    /// <summary>What the order reserves now (R40); 0 on a buy and when it is not live.</summary>
    public decimal ReservedQuantity { get; private set; }

    public string? Memo { get; private set; }

    public int PlacedByUserId { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R14: <c>Placed</c>, <c>Working</c> or <c>PartlyFilled</c>.</summary>
    public bool IsOpen => IsOpenStatus(Status);

    public static bool IsOpenStatus(OrderStatus status) =>
        status is OrderStatus.Placed or OrderStatus.Working or OrderStatus.PartlyFilled;

    /// <summary>R14: open, or with a fill that is not posted yet. Only a live order reserves.</summary>
    public bool IsLive => IsOpen;

    /// <summary>R14: <c>Quantity − FilledQuantity</c> while open, 0 once final.</summary>
    public decimal OpenQuantity => IsOpen ? Quantity - FilledQuantity : 0m;

    /// <summary>
    /// R6, R10–R13: a new order, <c>Placed</c>, with the price limit of <see cref="OrderRules"/> and its reservation:
    /// the most it can cost with fees (a buy) or its quantity (a sell).
    /// </summary>
    public static Order Place(
        int tenantId,
        int accountId,
        int instrumentId,
        OrderSide side,
        decimal quantity,
        string currency,
        decimal referencePrice,
        DateOnly referencePriceDate,
        OrderFeeTerms fees,
        string connection,
        string? memo,
        int placedByUserId,
        DateTimeOffset placedAt,
        Guid? publicId = null)
    {
        if (quantity <= 0m || decimal.Round(quantity, QuantityDecimals) != quantity)
        {
            throw new DomainException($"Quantity must be greater than 0 with at most {QuantityDecimals} decimal places.");
        }

        if (referencePrice <= 0m)
        {
            throw new DomainException("The reference price must be greater than 0.");
        }

        var quoteCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (quoteCurrency.Length != 3 || !quoteCurrency.All(char.IsAsciiLetter))
        {
            throw new DomainException("Currency is required.");
        }

        if (!BankConnectionName.IsValid(connection))
        {
            throw new DomainException("An order needs the name of a broker connection.");
        }

        if (fees.Schedule is { } schedule && !string.Equals(schedule.QuoteCurrency, quoteCurrency, StringComparison.Ordinal))
        {
            throw new DomainException("The trade fee schedule is for another quote currency.");
        }

        var order = new Order
        {
            TenantId = tenantId,
            AccountId = accountId,
            InstrumentId = instrumentId,
            Side = side,
            Type = OrderType.Market,
            TimeInForce = TimeInForce.Day,
            Quantity = quantity,
            Currency = quoteCurrency,
            ReferencePrice = referencePrice,
            ReferencePriceDate = referencePriceDate,
            PriceLimit = OrderRules.PriceLimit(side, referencePrice),
            TradeFeeScheduleId = fees.Schedule?.Id,
            Connection = connection,
            Status = OrderStatus.Placed,
            Memo = OptionalText(memo, MemoMaxLength, "Memo"),
            PlacedByUserId = placedByUserId,
            PlacedAt = placedAt.ToUniversalTime(),
            PublicId = publicId ?? Guid.NewGuid()
        };
        order.Reserve(fees);
        return order;
    }

    /// <summary>
    /// R39, R40: what the order reserves now, with <c>q</c> the quantity that can still be posted. A buy reserves the
    /// most <c>q</c> can cost at the price limit, plus the fee that amount adds to the fee already charged; a sell
    /// reserves <c>q</c>. Nothing when <c>q</c> is 0.
    /// </summary>
    private void Reserve(OrderFeeTerms fees)
    {
        var quantity = OpenQuantity;
        if (Side == OrderSide.Sell)
        {
            ReservedQuantity = quantity;
            ReservedCash = 0m;
            return;
        }

        ReservedQuantity = 0m;
        if (quantity == 0m)
        {
            ReservedCash = 0m;
            return;
        }

        var gross = OrderRules.MaximumGross(quantity, PriceLimit);
        ReservedCash = gross + fees.FeeFor(PostedGross + gross) - FeeCharged;
    }

    private static string? OptionalText(string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{label} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
