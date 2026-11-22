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

    internal static TransactionInterestDetail Create(int tenantId, Interest.InterestPeriod period, decimal accrued, decimal gross) =>
        new()
        {
            TenantId = tenantId,
            PeriodFrom = period.From,
            PeriodTo = period.To,
            Days = period.Days,
            AccruedAmount = accrued,
            GrossAmount = gross
        };

    /// <summary>A new row with the same values, for the Reversal (interest-run R27).</summary>
    internal TransactionInterestDetail Copy() =>
        Create(TenantId, new Interest.InterestPeriod(PeriodFrom, PeriodTo, Days), AccruedAmount, GrossAmount);
}
