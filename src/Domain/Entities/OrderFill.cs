namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One fill of an <see cref="Order"/> (docs/features/orders.md R26, R31, R43, §5): what the broker's <c>Fill</c>
/// report said. <c>Pending</c> until its Transaction is posted; settled when the broker's <c>Settled</c> report names it.
/// </summary>
public class OrderFill
{
    public const int BrokerReferenceMaxLength = 50;

    private OrderFill()
    {
    }

    public long Id { get; private set; }

    /// <summary><c>fillId</c> on the wire.</summary>
    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>The <c>Fill</c> report this fill came from; a report is the source of at most one fill.</summary>
    public long ExecutionReportId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly TradeDate { get; private set; }

    public DateOnly SettlementDate { get; private set; }

    /// <summary>The broker's contract note, or null.</summary>
    public string? BrokerReference { get; private set; }

    public OrderFillStatus Status { get; private set; }

    public FillHoldReason? HoldReason { get; private set; }

    public int? TransactionId { get; private set; }

    /// <summary>The Transaction's cash on the Account, signed: its principal leg plus its fee leg.</summary>
    public decimal? NetCash { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public DateOnly? SettledDate { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>Trade-settlement R6: the broker settlement the fill was settled in, from its <c>Settled</c>
    /// report.</summary>
    public string? BrokerSettlementId { get; private set; }

    /// <summary>Trade-settlement R8: the trade settlement the fill is in; at most one.</summary>
    public int? TradeSettlementId { get; private set; }

    /// <summary>Trade-settlement R27: when the broker took the fill back.</summary>
    public DateTimeOffset? BrokenAt { get; private set; }

    /// <summary>Trade-settlement R27: the <c>Broken</c> report; a report breaks at most one fill.</summary>
    public long? BreakReportId { get; private set; }

    public string? BreakReason { get; private set; }

    /// <summary>Trade-settlement R28–R31: what the platform did with the break.</summary>
    public FillBreakOutcome? BreakOutcome { get; private set; }

    /// <summary>Trade-settlement R30: why the fill's Transaction could not be reversed.</summary>
    public FillBreakHoldReason? BreakHoldReason { get; private set; }

    /// <summary>Trade-settlement R29: the Reversal the flow posted.</summary>
    public int? ReversalTransactionId { get; private set; }

    public int? BreakResolvedByUserId { get; private set; }

    public DateTimeOffset? BreakResolvedAt { get; private set; }

    public string? BreakNote { get; private set; }

    public bool IsBroken => Status == OrderFillStatus.Broken;

    public bool IsSettled => SettledAt is not null;

    internal static OrderFill Create(
        int tenantId,
        long executionReportId,
        decimal quantity,
        decimal price,
        DateOnly tradeDate,
        DateOnly settlementDate,
        string? brokerReference)
    {
        if (quantity <= 0m || price <= 0m)
        {
            throw new DomainException("A fill has a quantity and a price above 0.");
        }

        if (settlementDate < tradeDate)
        {
            throw new DomainException("A fill cannot settle before its trade date.");
        }

        var reference = brokerReference?.Trim();
        return new OrderFill
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            ExecutionReportId = executionReportId,
            Quantity = quantity,
            Price = price,
            TradeDate = tradeDate,
            SettlementDate = settlementDate,
            BrokerReference = string.IsNullOrEmpty(reference)
                ? null
                : reference.Length > BrokerReferenceMaxLength ? reference[..BrokerReferenceMaxLength] : reference,
            Status = OrderFillStatus.Pending
        };
    }

    /// <summary>R31: the fill's Transaction exists. Once, and only from <c>Pending</c>.</summary>
    public void MarkPosted(int transactionId, decimal netCash, DateTimeOffset at)
    {
        EnsurePending();
        Status = OrderFillStatus.Posted;
        HoldReason = null;
        TransactionId = transactionId;
        NetCash = netCash;
        PostedAt = at.ToUniversalTime();
    }

    /// <summary>R36: the fill cannot be posted now, for <paramref name="reason"/>; it stays <c>Pending</c>.</summary>
    public void Hold(FillHoldReason reason)
    {
        EnsurePending();
        HoldReason = reason;
    }

    /// <summary>R43: the broker said the fill has settled. Once; a fill can settle while it is still <c>Pending</c>.</summary>
    public void Settle(DateOnly date, DateTimeOffset at, string? brokerSettlementId = null)
    {
        if (IsSettled)
        {
            throw new DomainException("This fill has settled already.");
        }

        SettledAt = at.ToUniversalTime();
        SettledDate = date;
        BrokerSettlementId = brokerSettlementId;
    }

    /// <summary>
    /// Trade-settlement R27–R30: the broker took the fill back. Once, never after it settled. A <c>Pending</c> fill is
    /// <c>NotPosted</c>; a posted one is <c>Reversed</c> with its Reversal, or <c>NotApplied</c> with a hold reason.
    /// </summary>
    public void Break(
        long breakReportId,
        string? reason,
        DateTimeOffset at,
        FillBreakOutcome outcome,
        int? reversalTransactionId = null,
        FillBreakHoldReason? holdReason = null)
    {
        if (IsBroken)
        {
            throw new DomainException("This fill was broken already.");
        }

        if (IsSettled)
        {
            throw new DomainException("A fill that has settled cannot be broken.");
        }

        var fits = outcome switch
        {
            FillBreakOutcome.NotPosted => Status == OrderFillStatus.Pending && reversalTransactionId is null && holdReason is null,
            FillBreakOutcome.Reversed => Status == OrderFillStatus.Posted && reversalTransactionId is not null && holdReason is null,
            FillBreakOutcome.NotApplied => Status == OrderFillStatus.Posted && reversalTransactionId is null && holdReason is not null,
            _ => false
        };
        if (!fits)
        {
            throw new DomainException($"A {Status} fill cannot be broken as {outcome}.");
        }

        var trimmed = reason?.Trim();
        Status = OrderFillStatus.Broken;
        HoldReason = null;
        BrokenAt = at.ToUniversalTime();
        BreakReportId = breakReportId;
        BreakReason = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length > ExecutionReport.ReasonMaxLength ? trimmed[..ExecutionReport.ReasonMaxLength] : trimmed;
        BreakOutcome = outcome;
        BreakHoldReason = holdReason;
        ReversalTransactionId = reversalTransactionId;
    }

    /// <summary>Trade-settlement R31: a TenantAdmin corrected the account and resolves a break that could not be
    /// applied. Only from <c>NotApplied</c>.</summary>
    public void ResolveBreak(int byUserId, string note, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>Trade-settlement R8: the fill is put in a trade settlement, once.</summary>
    public void AssignTo(int tradeSettlementId)
    {
        if (TradeSettlementId is not null)
        {
            throw new DomainException("This fill is in a trade settlement already.");
        }

        TradeSettlementId = tradeSettlementId;
    }

    private void EnsurePending()
    {
        if (Status != OrderFillStatus.Pending)
        {
            throw new DomainException("This fill is posted already.");
        }
    }
}
