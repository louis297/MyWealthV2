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
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>R5: the account number upper-cased, without spaces and hyphens.</summary>
    public static string KeyOf(string accountNumber) => throw new NotImplementedException();

    public void Verify(int byUserId, string note, DateTimeOffset at) => throw new NotImplementedException();

    public void Reject(int byUserId, string note, DateTimeOffset at) => throw new NotImplementedException();

    public void Remove(int byUserId, string note, DateTimeOffset at) => throw new NotImplementedException();
}
