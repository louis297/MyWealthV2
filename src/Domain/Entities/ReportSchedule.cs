namespace MyWealthV2.Domain.Entities;

public class ReportSchedule : BaseAuditableEntity
{
    private ReportSchedule()
    {
    }

    public Guid PublicId { get; private set; }

    public int ReportId { get; private set; }

    public int TenantId { get; private set; }

    public string ParametersJson { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public string Cron { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = null!;

    public static ReportSchedule Create(
        int tenantId,
        int reportId,
        string parametersJson,
        bool isActive,
        string cron) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            ReportId = reportId,
            ParametersJson = parametersJson,
            IsActive = isActive,
            Cron = cron
        };

    public void Revise(string parametersJson, bool isActive, string cron)
    {
        ParametersJson = parametersJson;
        IsActive = isActive;
        Cron = cron;
    }

    public void Disable()
    {
        IsActive = false;
    }
}
