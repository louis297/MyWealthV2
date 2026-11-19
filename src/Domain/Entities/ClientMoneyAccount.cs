namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The trust bank account a Tenant holds Customers' cash in, for one currency
/// (docs/features/client-money-accounts.md §5). The four details never change; only disable and enable.
/// </summary>
public class ClientMoneyAccount : BaseAuditableEntity
{
    public const int BankNameMaxLength = 100;
    public const int AccountNameMaxLength = 140;
    public const int AccountNumberMaxLength = 34;

    private ClientMoneyAccount()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string BankName { get; private set; } = string.Empty;

    public string AccountName { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static ClientMoneyAccount Create(
        int tenantId,
        Currency currency,
        string bankName,
        string accountName,
        string accountNumber,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    public void Disable() => throw new NotImplementedException();

    public void Enable() => throw new NotImplementedException();
}
