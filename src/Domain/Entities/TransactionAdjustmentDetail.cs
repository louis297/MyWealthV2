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
}
