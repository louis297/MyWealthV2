using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The trust bank account a Tenant holds Customers' cash in, for one currency
/// (docs/features/client-money-accounts.md §5). The four details and the bank connection it is read through
/// (docs/features/deposit-matching.md R2) never change; only disable and enable.
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

    /// <summary>The configured bank connection its statement is read through (deposit-matching R2).</summary>
    public string BankConnection { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static ClientMoneyAccount Create(
        int tenantId,
        Currency currency,
        string bankName,
        string accountName,
        string accountNumber,
        string bankConnection,
        Guid? publicId = null)
    {
        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new DomainException("Currency is required.");
        }

        if (!currency.IsActive)
        {
            throw new DomainException("Currency must be enabled.");
        }

        if (!BankConnectionName.IsValid(bankConnection))
        {
            throw new DomainException(
                $"Bank connection must be 1 to {BankConnectionName.MaxLength} characters: a lower-case letter, then lower-case letters, digits and hyphens.");
        }

        return new ClientMoneyAccount
        {
            TenantId = tenantId,
            Currency = currency.Code.ToUpperInvariant(),
            BankName = NormaliseText(bankName, BankNameMaxLength, "Bank name"),
            AccountName = NormaliseText(accountName, AccountNameMaxLength, "Account name"),
            AccountNumber = NormaliseAccountNumber(accountNumber),
            BankConnection = bankConnection,
            PublicId = publicId ?? Guid.NewGuid(),
            IsActive = true
        };
    }

    /// <summary>True when <paramref name="accountNumber"/>, trimmed, is 1–34 characters of letters, digits,
    /// spaces and hyphens with no two spaces in a row (R2).</summary>
    public static bool IsValidAccountNumber(string? accountNumber)
    {
        var trimmed = accountNumber?.Trim() ?? string.Empty;
        return trimmed.Length is >= 1 and <= AccountNumberMaxLength
            && trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-')
            && !trimmed.Contains("  ", StringComparison.Ordinal);
    }

    public void Disable()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
    }

    public void Enable()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
    }

    private static string NormaliseText(string value, int maxLength, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < 1 || trimmed.Length > maxLength)
        {
            throw new DomainException($"{label} must be 1 to {maxLength} characters.");
        }

        return trimmed;
    }

    private static string NormaliseAccountNumber(string accountNumber)
    {
        if (!IsValidAccountNumber(accountNumber))
        {
            throw new DomainException(
                $"Account number must be 1 to {AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        }

        return accountNumber.Trim();
    }
}
