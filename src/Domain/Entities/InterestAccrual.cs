namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The interest one Custodial account earned on one UTC day (interest-run R16–R18): the day balance, the schedule
/// used, the effective annual rate and the amount (8 decimals). Written once and never changed, except that the
/// posting stamps it with its Transaction (R22).
/// </summary>
public class InterestAccrual
{
    private InterestAccrual()
    {
    }

    public long Id { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Balance { get; private set; }

    public int InterestRateScheduleId { get; private set; }

    public decimal EffectiveRatePercent { get; private set; }

    public decimal Amount { get; private set; }

    public int? PostedTransactionId { get; private set; }

    public static InterestAccrual Create(
        int tenantId,
        int accountId,
        DateOnly date,
        decimal balance,
        int interestRateScheduleId,
        decimal effectiveRatePercent,
        decimal amount)
    {
        if (balance <= 0m)
        {
            throw new DomainException("An accrual needs a day balance above 0.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("An accrual needs an amount above 0.");
        }

        return new InterestAccrual
        {
            TenantId = tenantId,
            AccountId = accountId,
            Date = date,
            Balance = balance,
            InterestRateScheduleId = interestRateScheduleId,
            EffectiveRatePercent = effectiveRatePercent,
            Amount = amount
        };
    }

    /// <summary>R22: stamps the row with the Transaction that paid it, once.</summary>
    public void MarkPosted(int transactionId)
    {
        if (PostedTransactionId is not null)
        {
            throw new DomainException("An accrual is posted once.");
        }

        PostedTransactionId = transactionId;
    }
}
