namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The period an interest posting pays (interest-run R26): first and last day, number of days, the accrued amount
/// (8 decimals) and the gross. Exactly one on an interest posting and on its Reversal; immutable.
/// </summary>
public class TransactionInterestDetail
{
    private TransactionInterestDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public int Days { get; private set; }

    public decimal AccruedAmount { get; private set; }

    public decimal GrossAmount { get; private set; }
}
