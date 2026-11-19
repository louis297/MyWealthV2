namespace MyWealthV2.Domain.Entities;

/// <summary>Why an adjustment was entered. One per <c>Adjustment</c> and per its Reversal; immutable (adjustments §5).</summary>
public class TransactionAdjustmentDetail
{
    private TransactionAdjustmentDetail()
    {
    }

    public int TenantId { get; private set; }

    public int TransactionId { get; private set; }

    public AdjustmentReason Reason { get; private set; }

    internal static TransactionAdjustmentDetail Create(int tenantId, AdjustmentReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Adjustment reason is not valid.");
        }

        return new TransactionAdjustmentDetail { TenantId = tenantId, Reason = reason };
    }

    /// <summary>A new row with the same reason, for the Reversal (adjustments R18).</summary>
    internal TransactionAdjustmentDetail Copy() => new() { TenantId = TenantId, Reason = Reason };
}
