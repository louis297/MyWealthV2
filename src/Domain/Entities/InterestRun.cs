namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One execution of the interest work for one Tenant and one as-of date (interest-run R30): the trigger, the person
/// (none for the daily job), the account (only for an account closure), start and end, and the counts. The source of
/// the Transactions it posts. It never changes after it ends.
/// </summary>
public class InterestRun : BaseAuditableEntity
{
    private InterestRun()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public DateOnly AsOfDate { get; private set; }

    public InterestRunTrigger Trigger { get; private set; }

    public int? RanByUserId { get; private set; }

    public int? AccountId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int AccountsProcessed { get; private set; }

    public int AccountsFailed { get; private set; }

    public int PostingCount { get; private set; }

    /// <summary>R21: the last day of the latest month that ended on or before the as-of date.</summary>
    public DateOnly PostingDate => PostingDateFor(AsOfDate);

    /// <summary>R21: <paramref name="asOfDate"/> when it is the last day of its month, else the last day of the month before.</summary>
    public static DateOnly PostingDateFor(DateOnly asOfDate)
    {
        var firstOfMonth = new DateOnly(asOfDate.Year, asOfDate.Month, 1);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        return asOfDate == lastOfMonth ? asOfDate : firstOfMonth.AddDays(-1);
    }

    public static InterestRun Start(
        int tenantId,
        DateOnly asOfDate,
        InterestRunTrigger trigger,
        int? ranByUserId,
        int? accountId,
        DateTimeOffset startedAt,
        Guid? publicId = null)
    {
        if (!Enum.IsDefined(trigger))
        {
            throw new DomainException("Unknown interest run trigger.");
        }

        if ((trigger == InterestRunTrigger.Scheduled) != (ranByUserId is null))
        {
            throw new DomainException("A scheduled run has no person; every other run names the person who started it.");
        }

        if ((trigger == InterestRunTrigger.AccountClosure) != (accountId is not null))
        {
            throw new DomainException("Only an account closure run names one account.");
        }

        return new InterestRun
        {
            PublicId = publicId ?? Guid.NewGuid(),
            TenantId = tenantId,
            AsOfDate = asOfDate,
            Trigger = trigger,
            RanByUserId = ranByUserId,
            AccountId = accountId,
            StartedAt = startedAt
        };
    }

    /// <summary>R30: the run ended with these counts; once.</summary>
    public void Complete(int accountsProcessed, int accountsFailed, int postingCount, DateTimeOffset completedAt)
    {
        if (CompletedAt is not null)
        {
            throw new DomainException("An interest run ends once.");
        }

        if (accountsProcessed < 0 || accountsFailed < 0 || postingCount < 0 || accountsFailed > accountsProcessed)
        {
            throw new DomainException("The counts of an interest run are not valid.");
        }

        AccountsProcessed = accountsProcessed;
        AccountsFailed = accountsFailed;
        PostingCount = postingCount;
        CompletedAt = completedAt;
    }
}
