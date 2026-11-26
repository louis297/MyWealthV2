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
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>R13: the run ended with these counts; once.</summary>
    public void Complete(int accountsProcessed, int accountsFailed, int daysValued, int daysIncomplete, DateTimeOffset completedAt) =>
        throw new NotImplementedException();
}
