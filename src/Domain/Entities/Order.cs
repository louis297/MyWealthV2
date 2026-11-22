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
    public const int BrokerOrderIdMaxLength = 100;
    public const int CloseReasonMaxLength = 140;

    /// <summary>R22: the close reason of an order the broker never had.</summary>
    public const string NotAtTheBrokerReason = "Not at the broker";

    /// <summary>R50: the note of the cancel request a corporate action makes.</summary>
    public const string CorporateActionNote = "Corporate action";

    public const string CancelAlreadyRequestedMessage = "A cancellation was already requested for this order.";

    private readonly List<OrderFill> _fills = [];

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

    /// <summary>Send attempts (R20).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>The failure of the last send that failed (R21); never <c>InvalidItem</c>.</summary>
    public BrokerFailure? LastFailure { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public string? BrokerOrderId { get; private set; }

    public OrderRejectReason? RejectReason { get; private set; }

    public DateTimeOffset? CancelRequestedAt { get; private set; }

    /// <summary>The person who asked; null for a request the platform made (R50).</summary>
    public int? CancelRequestedByUserId { get; private set; }

    public string? CancelNote { get; private set; }

    /// <summary>When the broker answered the cancel request (R22).</summary>
    public DateTimeOffset? CancelAnsweredAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The broker's text on a <c>Cancelled</c> or <c>Expired</c> order.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>The fills of R26, in the order they were added.</summary>
    public IReadOnlyList<OrderFill> Fills => _fills;

    public bool HasCancelRequest => CancelRequestedAt is not null;

    /// <summary>R18: <c>Placed</c> with no send attempt, so the broker cannot hold it.</summary>
    public bool IsUnsent => throw new NotImplementedException();

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

    /// <summary>R20: one more send attempt, recorded before the call. Only a <c>Placed</c> order with no cancel
    /// request is sent.</summary>
    public void RecordAttempt(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R21: the broker accepted it: <c>Placed</c> becomes <c>Working</c>. An order a report already moved on
    /// keeps its status and only takes the broker's order id.</summary>
    public void Accept(string brokerOrderId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R21: the broker refused it: <c>Placed</c> becomes <c>Rejected</c> and reserves nothing.</summary>
    public void Reject(OrderRejectReason reason, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R21: the send failed; the order stays as it is and is sent again.</summary>
    public void RecordFailure(BrokerFailure failure) => throw new NotImplementedException();

    /// <summary>R18: an order the broker cannot hold is <c>Cancelled</c> at once and reserves nothing.</summary>
    public void CancelUnsent(int? byUserId, string? note, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R18: an open order the broker may hold gets a cancel request; its status does not change.</summary>
    public void RequestCancel(int? byUserId, string? note, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R22: the broker answered the cancel request.</summary>
    public void AnswerCancel(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R22: the broker never had the order: it is <c>Cancelled</c> with <see cref="NotAtTheBrokerReason"/>.</summary>
    public void EndAsUnknown(DateTimeOffset at, OrderFeeTerms fees) => throw new NotImplementedException();

    /// <summary>R26, R28: adds a <c>Pending</c> fill within <c>Quantity − FilledQuantity</c>, whatever the status, and
    /// moves the status: <c>Filled</c> when the whole quantity is filled, else an ended order stays ended and an open
    /// one is <c>PartlyFilled</c>.</summary>
    public OrderFill AddFill(
        long executionReportId,
        decimal quantity,
        decimal price,
        DateOnly tradeDate,
        DateOnly settlementDate,
        string? brokerReference,
        DateTimeOffset at,
        OrderFeeTerms fees) => throw new NotImplementedException();

    /// <summary>R27: the broker ended an open order: <c>Cancelled</c> or <c>Expired</c>, with its reason.</summary>
    public void End(OrderStatus status, string? reason, DateTimeOffset at, OrderFeeTerms fees) =>
        throw new NotImplementedException();

    /// <summary>R33: the fee of a fill of <paramref name="gross"/>: the schedule's fee on the posted gross with this
    /// fill, minus the fee already charged.</summary>
    public decimal FeeOfFill(decimal gross, OrderFeeTerms fees) => throw new NotImplementedException();

    /// <summary>R31: a fill of the order was posted with <paramref name="gross"/> and <paramref name="fee"/>.</summary>
    public void RecordPostedFill(decimal gross, decimal fee, OrderFeeTerms fees) => throw new NotImplementedException();

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
