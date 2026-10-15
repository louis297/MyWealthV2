namespace MyWealthV2.Domain.Entities;

public class ReportExecution : BaseAuditableEntity
{
    private ReportExecution()
    {
    }

    public Guid PublicId { get; private set; }

    public int ReportId { get; private set; }

    public int? ScheduleId { get; private set; }

    public int TenantId { get; private set; }

    public string TriggerKind { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string ParametersJson { get; private set; } = string.Empty;

    public DateTimeOffset? AsOf { get; private set; }

    public int? TriggeredByUserId { get; private set; }

    public string? ResultStorageKey { get; private set; }

    public string? ErrorMessage { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static ReportExecution Queue(
        int tenantId,
        int reportId,
        string triggerKind,
        string parametersJson,
        int? triggeredByUserId,
        DateTimeOffset? asOf = null,
        int? scheduleId = null) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            ReportId = reportId,
            ScheduleId = scheduleId,
            TriggerKind = triggerKind,
            Status = "Queued",
            ParametersJson = parametersJson,
            AsOf = asOf,
            TriggeredByUserId = triggeredByUserId
        };
}
