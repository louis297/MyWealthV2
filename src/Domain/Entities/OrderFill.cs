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

    public bool IsSettled => SettledAt is not null;

    internal static OrderFill Create(
        int tenantId,
        long executionReportId,
        decimal quantity,
        decimal price,
        DateOnly tradeDate,
        DateOnly settlementDate,
        string? brokerReference) => throw new NotImplementedException();

    /// <summary>R31: the fill's Transaction exists. Once, and only from <c>Pending</c>.</summary>
    public void MarkPosted(int transactionId, decimal netCash, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R36: the fill cannot be posted now, for <paramref name="reason"/>; it stays <c>Pending</c>.</summary>
    public void Hold(FillHoldReason reason) => throw new NotImplementedException();

    /// <summary>R43: the broker said the fill has settled. Once; a fill can settle while it is still <c>Pending</c>.</summary>
    public void Settle(DateOnly date, DateTimeOffset at) => throw new NotImplementedException();
}
