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
    public bool IsOpen => IsOpenStatus(Status);

    public static bool IsOpenStatus(WithdrawalStatus status) =>
        status is WithdrawalStatus.Requested or WithdrawalStatus.Approved or WithdrawalStatus.Sent;

    /// <summary>R23: 1–18 letters, digits, spaces, hyphens and full stops, trimmed.</summary>
    public static bool IsValidReference(string? reference)
    {
        var trimmed = reference?.Trim() ?? string.Empty;
        return trimmed.Length is >= 1 and <= ReferenceMaxLength
               && trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '.');
    }

    /// <summary>R18–R23: a new withdrawal, <c>Requested</c>, reserving <paramref name="amount"/>.</summary>
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
        Guid? publicId = null)
    {
        if (amount <= 0)
        {
            throw new DomainException("Amount must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency is required.");
        }

        if (!IsValidReference(reference))
        {
            throw new DomainException(
                $"Reference must be 1 to {ReferenceMaxLength} letters, digits, spaces, hyphens and full stops.");
        }

        return new Withdrawal
        {
            TenantId = tenantId,
            AccountId = accountId,
            NominatedBankAccountId = nominatedBankAccountId,
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            Reference = reference.Trim(),
            Memo = OptionalText(memo, MemoMaxLength, "Memo"),
            Status = WithdrawalStatus.Requested,
            RequestedByUserId = requestedByUserId,
            RequestedAt = requestedAt,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    /// <summary>R28, R29: a TenantAdmin other than the requester approves it.</summary>
    public void Approve(int byUserId, string? note, DateTimeOffset at)
    {
        EnsureStatus("approved", WithdrawalStatus.Requested);
        EnsureNotRequester(byUserId);
        Decide(WithdrawalDecision.Approved, byUserId, OptionalText(note, NoteMaxLength, "Note"), at);
        Status = WithdrawalStatus.Approved;
    }

    /// <summary>R27, R28: a TenantAdmin other than the requester refuses it, with a note. The reservation ends.</summary>
    public void Reject(int byUserId, string note, DateTimeOffset at)
    {
        EnsureStatus("rejected", WithdrawalStatus.Requested);
        EnsureNotRequester(byUserId);
        var trimmed = OptionalText(note, NoteMaxLength, "Note") ?? throw new DomainException("Note is required.");
        Decide(WithdrawalDecision.Rejected, byUserId, trimmed, at);
        Status = WithdrawalStatus.Rejected;
    }

    /// <summary>R30: withdrawn before the bank was asked. Application checks that the payment has no attempt.</summary>
    public void Cancel(int byUserId, string? note, DateTimeOffset at)
    {
        EnsureStatus("cancelled", WithdrawalStatus.Requested, WithdrawalStatus.Approved);
        Decide(WithdrawalDecision.Cancelled, byUserId, OptionalText(note, NoteMaxLength, "Note"), at);
        Status = WithdrawalStatus.Cancelled;
    }

    /// <summary>R33: the bank accepted the payment.</summary>
    public void MarkSent()
    {
        EnsureStatus("sent", WithdrawalStatus.Approved);
        Status = WithdrawalStatus.Sent;
    }

    /// <summary>R33: the bank refused the payment. The reservation ends.</summary>
    public void MarkFailed(PaymentRejectReason reason)
    {
        EnsureStatus("failed", WithdrawalStatus.Approved);
        Status = WithdrawalStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>R40: the statement showed the payment and the payout is posted. The reservation ends.</summary>
    public void MarkPaid(int payoutTransactionId)
    {
        EnsureStatus("paid", WithdrawalStatus.Approved, WithdrawalStatus.Sent);
        Status = WithdrawalStatus.Paid;
        PayoutTransactionId = payoutTransactionId;
    }

    /// <summary>R42: the payee's bank sent the money back and the payout is reversed.</summary>
    public void MarkReturned(int returnTransactionId, string? reason)
    {
        EnsureStatus("returned", WithdrawalStatus.Paid);
        Status = WithdrawalStatus.Returned;
        ReturnTransactionId = returnTransactionId;
        var trimmed = reason?.Trim();
        ReturnReason = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length > ReturnReasonMaxLength ? trimmed[..ReturnReasonMaxLength] : trimmed;
    }

    private void Decide(WithdrawalDecision action, int byUserId, string? note, DateTimeOffset at)
    {
        DecisionAction = action;
        DecisionByUserId = byUserId;
        DecisionAt = at;
        DecisionNote = note;
    }

    private void EnsureNotRequester(int byUserId)
    {
        if (byUserId == RequestedByUserId)
        {
            throw new WithdrawalStatusException(FourEyesMessage);
        }
    }

    private void EnsureStatus(string action, params WithdrawalStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new WithdrawalStatusException($"This withdrawal is {Status} and cannot be {action}.");
        }
    }

    private static string? OptionalText(string? value, int maxLength, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{label} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
