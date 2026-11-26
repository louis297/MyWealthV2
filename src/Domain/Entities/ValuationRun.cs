namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One execution of the valuation work for one Tenant and one through date (valuation-run R13): the trigger, the
/// person (none for the daily job), start and end, and the counts. Each stored valuation names the run that stored
/// it. It never changes after it ends.
/// </summary>
public class ValuationRun : BaseAuditableEntity
{
    private ValuationRun()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public DateOnly ThroughDate { get; private set; }

    public ValuationRunTrigger Trigger { get; private set; }

    public int? RanByUserId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int AccountsProcessed { get; private set; }

    public int AccountsFailed { get; private set; }

    public int DaysValued { get; private set; }

    public int DaysIncomplete { get; private set; }

    public static ValuationRun Start(
        int tenantId,
        DateOnly throughDate,
        ValuationRunTrigger trigger,
        int? ranByUserId,
        DateTimeOffset startedAt,
        Guid? publicId = null)
    {
        if (!Enum.IsDefined(trigger))
        {
            throw new DomainException("Unknown valuation run trigger.");
        }

        if ((trigger == ValuationRunTrigger.Scheduled) != (ranByUserId is null))
        {
            throw new DomainException("A scheduled run has no person; a manual run names the person who started it.");
        }

        return new ValuationRun
        {
            PublicId = publicId ?? Guid.NewGuid(),
            TenantId = tenantId,
            ThroughDate = throughDate,
            Trigger = trigger,
            RanByUserId = ranByUserId,
            StartedAt = startedAt
        };
    }

    /// <summary>R13: the run ended with these counts; once.</summary>
    public void Complete(int accountsProcessed, int accountsFailed, int daysValued, int daysIncomplete, DateTimeOffset completedAt)
    {
        if (CompletedAt is not null)
        {
            throw new DomainException("A valuation run ends once.");
        }

        if (accountsProcessed < 0 || accountsFailed < 0 || daysValued < 0 || daysIncomplete < 0
            || accountsFailed > accountsProcessed || daysIncomplete > daysValued)
        {
            throw new DomainException("The counts of a valuation run are not valid.");
        }

        AccountsProcessed = accountsProcessed;
        AccountsFailed = accountsFailed;
        DaysValued = daysValued;
        DaysIncomplete = daysIncomplete;
        CompletedAt = completedAt;
    }
}
