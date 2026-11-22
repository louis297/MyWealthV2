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
    public static DateOnly PostingDateFor(DateOnly asOfDate) => throw new NotImplementedException();

    public static InterestRun Start(
        int tenantId,
        DateOnly asOfDate,
        InterestRunTrigger trigger,
        int? ranByUserId,
        int? accountId,
        DateTimeOffset startedAt,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>R30: the run ended with these counts; once.</summary>
    public void Complete(int accountsProcessed, int accountsFailed, int postingCount, DateTimeOffset completedAt) =>
        throw new NotImplementedException();
}
