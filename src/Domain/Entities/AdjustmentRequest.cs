namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A Tenant's record of one adjustment a person asks for (docs/features/adjustment-review.md R2, R6, §5). Not a
/// Transaction: it changes no balance, holding or reservation until a second person approves it, and the approval
/// posts the <c>Adjustment</c>. The fields never change after the request is stored.
/// </summary>
public class AdjustmentRequest : BaseAuditableEntity
{
    public const int MemoMaxLength = 200;
    public const int ReferenceMaxLength = 100;
    public const int NoteMaxLength = 200;

    public const string FourEyesMessage =
        "An adjustment must be approved by someone other than the person who requested it.";

    private AdjustmentRequest()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public AdjustmentReason Reason { get; private set; }

    public string Memo { get; private set; } = string.Empty;

    public string? Reference { get; private set; }

    /// <summary>The cash part in the Account currency; null when the request has none.</summary>
    public decimal? CashAmount { get; private set; }

    /// <summary>The holding part: the Instrument with both changes, or all three null.</summary>
    public int? InstrumentId { get; private set; }

    public decimal? QuantityChange { get; private set; }

    public decimal? CostChange { get; private set; }

    public AdjustmentRequestStatus Status { get; private set; }

    public int RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The end status the decision led to; null while <c>Requested</c>.</summary>
    public AdjustmentRequestStatus? DecisionAction { get; private set; }

    public int? DecisionByUserId { get; private set; }

    public DateTimeOffset? DecisionAt { get; private set; }

    public string? DecisionNote { get; private set; }

    /// <summary>The posted <c>Adjustment</c>; set exactly when <c>Approved</c>.</summary>
    public int? TransactionId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public bool IsOpen => Status == AdjustmentRequestStatus.Requested;

    /// <summary>R3: a new request, <c>Requested</c>. <paramref name="holding"/> is the Instrument and the two
    /// changes, or null.</summary>
    public static AdjustmentRequest Request(
        int tenantId,
        int accountId,
        AdjustmentReason reason,
        string memo,
        string? reference,
        decimal? cashAmount,
        (int InstrumentId, decimal QuantityChange, decimal CostChange)? holding,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        Guid? publicId = null)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Adjustment reason is not valid.");
        }

        if (cashAmount is null && holding is null)
        {
            throw new DomainException("An adjustment request needs a cash amount, a holding change or both.");
        }

        if (cashAmount == 0m)
        {
            throw new DomainException("Amount cannot be 0.");
        }

        if (holding is { QuantityChange: 0m, CostChange: 0m })
        {
            throw new DomainException("A holding change needs a quantity change or a cost change.");
        }

        return new AdjustmentRequest
        {
            TenantId = tenantId,
            AccountId = accountId,
            Reason = reason,
            Memo = Optional(memo, MemoMaxLength, "Memo") ?? throw new DomainException($"Memo must be 1 to {MemoMaxLength} characters."),
            Reference = Optional(reference, ReferenceMaxLength, "Reference"),
            CashAmount = cashAmount,
            InstrumentId = holding?.InstrumentId,
            QuantityChange = holding?.QuantityChange,
            CostChange = holding?.CostChange,
            Status = AdjustmentRequestStatus.Requested,
            RequestedByUserId = requestedByUserId,
            RequestedAt = requestedAt,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    /// <summary>R8, R9: a person other than the requester approves it; the posted Transaction is kept.</summary>
    public void Approve(int byUserId, DateTimeOffset at, string? note, int transactionId)
    {
        EnsureRequested("approved");
        EnsureNotRequester(byUserId);
        Decide(AdjustmentRequestStatus.Approved, byUserId, at, Optional(note, NoteMaxLength, "Note"));
        TransactionId = transactionId;
    }

    /// <summary>R8, R10: a person other than the requester refuses it, with a note.</summary>
    public void Reject(int byUserId, DateTimeOffset at, string note)
    {
        EnsureRequested("rejected");
        EnsureNotRequester(byUserId);
        var trimmed = Optional(note, NoteMaxLength, "Note") ?? throw new DomainException("Note is required.");
        Decide(AdjustmentRequestStatus.Rejected, byUserId, at, trimmed);
    }

    /// <summary>R11: any holder of the permission withdraws it, the requester included.</summary>
    public void Cancel(int byUserId, DateTimeOffset at, string? note)
    {
        EnsureRequested("cancelled");
        Decide(AdjustmentRequestStatus.Cancelled, byUserId, at, Optional(note, NoteMaxLength, "Note"));
    }

    private void Decide(AdjustmentRequestStatus status, int byUserId, DateTimeOffset at, string? note)
    {
        DecisionAction = status;
        DecisionByUserId = byUserId;
        DecisionAt = at;
        DecisionNote = note;
        Status = status;
    }

    private void EnsureNotRequester(int byUserId)
    {
        if (byUserId == RequestedByUserId)
        {
            throw new AdjustmentRequestStatusException(FourEyesMessage);
        }
    }

    private void EnsureRequested(string action)
    {
        if (Status != AdjustmentRequestStatus.Requested)
        {
            throw new AdjustmentRequestStatusException(StatusMessage(Status, action));
        }
    }

    /// <summary>R7: the refusal of an action on a request that is not <c>Requested</c>.</summary>
    public static string StatusMessage(AdjustmentRequestStatus status, string action) =>
        $"This adjustment request is {status} and cannot be {action}.";

    private static string? Optional(string? value, int maxLength, string label)
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
