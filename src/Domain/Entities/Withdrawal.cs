namespace MyWealthV2.Domain.Entities;

/// <summary>
/// An instruction to pay cash from a Custodial account to a verified nominated bank account of the same Customer
/// (docs/features/withdrawals.md §4.2, §5; roadmap D3). Not a Transaction: its payout is posted when the bank
/// statement shows the payment. Open withdrawals reserve their amount (R24, R35).
/// </summary>
public class Withdrawal : BaseAuditableEntity
{
    public const int ReferenceMaxLength = 18;
    public const int MemoMaxLength = 200;
    public const int NoteMaxLength = 200;
    public const int ReturnReasonMaxLength = 140;

    public const string FourEyesMessage = "A withdrawal must be approved by someone other than the person who requested it.";

    private Withdrawal()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int NominatedBankAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string Reference { get; private set; } = string.Empty;

    public string? Memo { get; private set; }

    public WithdrawalStatus Status { get; private set; }

    public int RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public WithdrawalDecision? DecisionAction { get; private set; }

    public int? DecisionByUserId { get; private set; }

    public DateTimeOffset? DecisionAt { get; private set; }

    public string? DecisionNote { get; private set; }

    public int? PayoutTransactionId { get; private set; }

    public int? ReturnTransactionId { get; private set; }

    public PaymentRejectReason? FailureReason { get; private set; }

    public string? ReturnReason { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R24: only an open withdrawal reserves cash.</summary>
    public bool IsOpen => throw new NotImplementedException();

    public static bool IsOpenStatus(WithdrawalStatus status) => throw new NotImplementedException();

    /// <summary>R23: 1–18 letters, digits, spaces, hyphens and full stops, trimmed.</summary>
    public static bool IsValidReference(string? reference) => throw new NotImplementedException();

    public static Withdrawal Request(
        int tenantId,
        int accountId,
        int nominatedBankAccountId,
        decimal amount,
        string currency,
        string reference,
        string? memo,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    public void Approve(int byUserId, string? note, DateTimeOffset at) => throw new NotImplementedException();

    public void Reject(int byUserId, string note, DateTimeOffset at) => throw new NotImplementedException();

    public void Cancel(int byUserId, string? note, DateTimeOffset at) => throw new NotImplementedException();

    public void MarkSent() => throw new NotImplementedException();

    public void MarkFailed(PaymentRejectReason reason) => throw new NotImplementedException();

    public void MarkPaid(int payoutTransactionId) => throw new NotImplementedException();

    public void MarkReturned(int returnTransactionId, string? reason) => throw new NotImplementedException();
}
