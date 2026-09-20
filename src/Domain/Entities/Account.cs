namespace MyWealthV2.Domain.Entities;

public class Account : BaseAuditableEntity
{
    private Account()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int CustomerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }

    public AccountStatus Status { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public bool IsActive
    {
        get => Status == AccountStatus.Open;
        private set { }
    }

    public byte[] RowVersion { get; private set; } = null!;
}
