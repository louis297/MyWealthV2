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
        EnsureCurrency(currency);

        var account = new Account
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Name = NormaliseName(name),
            Type = type,
            Currency = currency.Code,
            PublicId = publicId ?? Guid.NewGuid(),
            Status = AccountStatus.Open
        };
        account.AddDomainEvent(new AccountOpened(account));
        return account;
    }

    public void Rename(string name)
    {
        Name = NormaliseName(name);
    }

    public void Close()
    {
        if (Status == AccountStatus.Closed)
        {
            return;
        }

        Status = AccountStatus.Closed;
        AddDomainEvent(new AccountClosed(this));
    }

    public void Reopen()
    {
        if (Status == AccountStatus.Open)
        {
            return;
        }

        Status = AccountStatus.Open;
        AddDomainEvent(new AccountReopened(this));
    }

    private static string NormaliseName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 200)
        {
            throw new DomainException("Name is required.");
        }

        return trimmed;
    }

    private static void EnsureCurrency(Currency currency)
    {
        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new DomainException("Currency is required.");
        }
    }
}
