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

    public static Account Create(
        int tenantId,
        int customerId,
        string name,
        AccountType type,
        Currency currency,
        Guid? publicId = null)
    {
        return new Account
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Name = name,
            Type = type,
            Currency = currency.Code,
            PublicId = publicId ?? Guid.NewGuid(),
            Status = AccountStatus.Open
        };
    }

    public void Rename(string name)
    {
    }

    public void Close()
    {
    }

    public void Reopen()
    {
    }
}
