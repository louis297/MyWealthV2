namespace MyWealthV2.Domain.Entities;

public class TenantReport : BaseEntity
{
    private TenantReport()
    {
    }

    public int TenantId { get; private set; }

    public int ReportId { get; private set; }
}
