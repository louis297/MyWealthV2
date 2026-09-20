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

    public static Instrument Create(
        int tenantId,
        string symbol,
        string name,
        Currency quoteCurrency,
        Guid? publicId = null)
    {
        return new Instrument
        {
            TenantId = tenantId,
            Symbol = symbol,
            Name = name,
            QuoteCurrency = quoteCurrency.Code,
            PublicId = publicId ?? Guid.NewGuid(),
            IsEnabled = true
        };
    }

    public void Rename(string? name, string? symbol)
    {
    }

    public void Disable()
    {
    }

    public void Enable()
    {
    }
}
