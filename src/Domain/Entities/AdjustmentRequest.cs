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
        Guid? publicId = null) =>
        throw new NotImplementedException();

    /// <summary>R8, R9: a person other than the requester approves it; the posted Transaction is kept.</summary>
    public void Approve(int byUserId, DateTimeOffset at, string? note, int transactionId) =>
        throw new NotImplementedException();

    /// <summary>R8, R10: a person other than the requester refuses it, with a note.</summary>
    public void Reject(int byUserId, DateTimeOffset at, string note) => throw new NotImplementedException();

    /// <summary>R11: any holder of the permission withdraws it, the requester included.</summary>
    public void Cancel(int byUserId, DateTimeOffset at, string? note) => throw new NotImplementedException();
}
