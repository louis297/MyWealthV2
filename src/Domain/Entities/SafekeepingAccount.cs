using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The account at a custodian in which a firm holds, together, the securities quoted in one currency of all its
/// Custodial accounts (docs/features/in-kind-transfers.md R3–R5). The details and the custodian connection never
/// change; only disable and enable. One active per Tenant and currency is held by Application and an index.
/// </summary>
public class SafekeepingAccount : BaseAuditableEntity
{
    public const int CustodianNameMaxLength = 100;
    public const int AccountNameMaxLength = 140;
    public const int AccountNumberMaxLength = ClientMoneyAccount.AccountNumberMaxLength;

    private SafekeepingAccount()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string CustodianName { get; private set; } = string.Empty;

    public string AccountName { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    /// <summary>The configured custodian connection its feed is read through and its transfers are sent on (R2, R3).</summary>
    public string CustodianConnection { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static SafekeepingAccount Create(
        int tenantId,
        Currency currency,
        string custodianName,
        string accountName,
        string accountNumber,
        string custodianConnection,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    public void Disable() => throw new NotImplementedException();

    public void Enable() => throw new NotImplementedException();
}
