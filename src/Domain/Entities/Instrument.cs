namespace MyWealthV2.Domain.Entities;

public class Instrument : BaseAuditableEntity
{
    private Instrument()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string QuoteCurrency { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;
}
