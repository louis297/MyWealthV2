namespace MyWealthV2.Domain.Entities;

/// <summary>Why an adjustment was entered. One per <c>Adjustment</c>, and one per Reversal of an adjustment posted
/// before adjustment review; immutable (adjustments §5). An adjustment is no longer reversed (adjustment-review R15).</summary>
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
}
