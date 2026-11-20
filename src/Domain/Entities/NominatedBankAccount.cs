namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A Customer's own bank account that withdrawals may be paid to (docs/features/withdrawals.md §4.1, §5). The
/// details never change (R6); a TenantAdmin other than the person who added it verifies or rejects it (R9).
/// </summary>
public class NominatedBankAccount : BaseAuditableEntity
{
    public const int BankNameMaxLength = 100;
    public const int AccountNameMaxLength = 140;
    public const int AccountNumberMaxLength = 34;
    public const int NoteMaxLength = 200;

    private NominatedBankAccount()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int CustomerId { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string BankName { get; private set; } = string.Empty;

    public string AccountName { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public string AccountNumberKey { get; private set; } = string.Empty;

    public NominatedBankAccountStatus Status { get; private set; }

    public int AddedByUserId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public int? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNote { get; private set; }

    public int? RemovedByUserId { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public string? RemovedNote { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static NominatedBankAccount Add(
        int tenantId,
        int customerId,
        Currency currency,
        string bankName,
        string accountName,
        string accountNumber,
        int addedByUserId,
        DateTimeOffset addedAt,
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

        if (!ClientMoneyAccount.IsValidAccountNumber(accountNumber))
        {
            throw new DomainException(
                $"Account number must be 1 to {AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        }

        var number = accountNumber.Trim();
        return new NominatedBankAccount
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Currency = currency.Code.ToUpperInvariant(),
            BankName = Text(bankName, BankNameMaxLength, "Bank name"),
            AccountName = Text(accountName, AccountNameMaxLength, "Account name"),
            AccountNumber = number,
            AccountNumberKey = KeyOf(number),
            Status = NominatedBankAccountStatus.Pending,
            AddedByUserId = addedByUserId,
            AddedAt = addedAt,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    /// <summary>R5: the account number upper-cased, without spaces and hyphens.</summary>
    public static string KeyOf(string accountNumber) =>
        new string(accountNumber.Where(ch => ch is not (' ' or '-')).ToArray()).ToUpperInvariant();

    /// <summary>R7, R9: a TenantAdmin other than the adder checked the evidence.</summary>
    public void Verify(int byUserId, string note, DateTimeOffset at) =>
        Review(NominatedBankAccountStatus.Verified, "verified", byUserId, note, at);

    /// <summary>R7, R9: a TenantAdmin other than the adder refused it.</summary>
    public void Reject(int byUserId, string note, DateTimeOffset at) =>
        Review(NominatedBankAccountStatus.Rejected, "rejected", byUserId, note, at);

    /// <summary>R7, R14: no longer used. Withdrawals already approved to it are still paid.</summary>
    public void Remove(int byUserId, string note, DateTimeOffset at)
    {
        EnsureStatus("removed", NominatedBankAccountStatus.Pending, NominatedBankAccountStatus.Verified);
        var trimmed = Note(note);
        Status = NominatedBankAccountStatus.Removed;
        RemovedByUserId = byUserId;
        RemovedAt = at;
        RemovedNote = trimmed;
    }

    private void Review(NominatedBankAccountStatus to, string action, int byUserId, string note, DateTimeOffset at)
    {
        EnsureStatus(action, NominatedBankAccountStatus.Pending);
        if (byUserId == AddedByUserId)
        {
            throw new NominatedBankAccountStatusException(
                "A bank account must be verified by someone other than the person who added it.");
        }

        var trimmed = Note(note);
        Status = to;
        ReviewedByUserId = byUserId;
        ReviewedAt = at;
        ReviewNote = trimmed;
    }

    private void EnsureStatus(string action, params NominatedBankAccountStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new NominatedBankAccountStatusException($"This bank account is {Status} and cannot be {action}.");
        }
    }

    private static string Note(string note)
    {
        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > NoteMaxLength)
        {
            throw new DomainException($"Note must be 1 to {NoteMaxLength} characters.");
        }

        return trimmed;
    }

    private static string Text(string value, int maxLength, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < 1 || trimmed.Length > maxLength)
        {
            throw new DomainException($"{label} must be 1 to {maxLength} characters.");
        }

        return trimmed;
    }
}
