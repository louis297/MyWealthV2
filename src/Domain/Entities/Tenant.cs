namespace MyWealthV2.Domain.Entities;

public class Tenant : BaseAuditableEntity
{
    private Tenant()
    {
    }

    public Guid PublicId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static Tenant Create(string name, string code, Guid? publicId = null)
    {
        return new Tenant
        {
            Name = name,
            Code = code,
            PublicId = publicId ?? Guid.NewGuid(),
            IsEnabled = true
        };
    }

    public void Disable()
    {
        IsEnabled = false;
    }
}
