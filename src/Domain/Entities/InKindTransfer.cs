namespace MyWealthV2.Domain.Entities;

/// <summary>
/// An instruction to move a quantity of one Instrument between one Custodial account and a counterparty, with no
/// money paid for it (docs/features/in-kind-transfers.md R20–R36). Not a Transaction: the custody movement that
/// confirms it posts the <c>StockIn</c> or <c>StockOut</c>. An open transfer <c>Out</c> reserves its quantity.
/// </summary>
public class InKindTransfer : BaseAuditableEntity
{
    public const int CounterpartyNameMaxLength = 100;
    public const int CounterpartyHolderNameMaxLength = 140;
    public const int MemoMaxLength = 200;
    public const int NoteMaxLength = 200;
    public const int CustodianTransferIdMaxLength = 100;

    public const string FourEyesMessage =
        "A transfer of securities must be approved by someone other than the person who requested it.";

    public const string CancelAfterAttemptMessage =
        "The custodian has been asked to make this transfer, so it can no longer be cancelled.";

    private InKindTransfer()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int InstrumentId { get; private set; }

    public TransferDirection Direction { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>The Instrument's quote currency when requested.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>R25: the total cost of the quantity in the quote currency; a transfer <c>In</c> only.</summary>
    public decimal? Cost { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    public bool ChangesBeneficialOwner { get; private set; }

    public string CounterpartyName { get; private set; } = string.Empty;

    public string CounterpartyAccount { get; private set; } = string.Empty;

    public string CounterpartyHolderName { get; private set; } = string.Empty;

    public string? Memo { get; private set; }

    public InKindTransferStatus Status { get; private set; }

    public int RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public InKindTransferDecision? DecisionAction { get; private set; }

    public int? DecisionByUserId { get; private set; }

    public DateTimeOffset? DecisionAt { get; private set; }

    public string? DecisionNote { get; private set; }

    /// <summary>R31: stored by the approval and never changed.</summary>
    public int? SafekeepingAccountId { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public CustodianFailure? LastFailure { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public string? CustodianTransferId { get; private set; }

    public TransferRejectReason? FailureReason { get; private set; }

    public int? SetAsideByUserId { get; private set; }

    public DateTimeOffset? SetAsideAt { get; private set; }

    public string? SetAsideNote { get; private set; }

    public int? TransactionId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R27: <c>Requested</c>, <c>Approved</c> or <c>Sent</c>.</summary>
    public bool IsOpen => throw new NotImplementedException();

    /// <summary>R26, R43: the quantity of an open transfer <c>Out</c>, else 0.</summary>
    public decimal ReservedQuantity => throw new NotImplementedException();

    public static bool IsOpenStatus(InKindTransferStatus status) => throw new NotImplementedException();

    /// <summary>R21, R25: a new transfer, <c>Requested</c>.</summary>
    public static InKindTransfer Request(
        int tenantId,
        int accountId,
        int instrumentId,
        TransferDirection direction,
        decimal quantity,
        string currency,
        decimal? cost,
        DateOnly? acquiredOn,
        bool changesBeneficialOwner,
        string counterpartyName,
        string counterpartyAccount,
        string counterpartyHolderName,
        string? memo,
        int requestedByUserId,
        DateTimeOffset requestedAt,
        Guid? publicId = null) =>
        throw new NotImplementedException();

    public void Approve(int byUserId, DateTimeOffset at, string? note, int safekeepingAccountId) =>
        throw new NotImplementedException();

    public void Reject(int byUserId, DateTimeOffset at, string note) => throw new NotImplementedException();

    public void Cancel(int byUserId, DateTimeOffset at, string? note) => throw new NotImplementedException();

    /// <summary>R34: an attempt to send is recorded and committed before the call.</summary>
    public void RecordAttempt(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R35: the custodian accepted the instruction.</summary>
    public void Accept(string custodianTransferId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R35: the custodian refused the instruction.</summary>
    public void Fail(TransferRejectReason reason, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R35: the call failed; the transfer stays <c>Approved</c> and is sent again.</summary>
    public void RecordFailure(CustodianFailure failure) => throw new NotImplementedException();

    public void SetAside(int byUserId, DateTimeOffset at, string note) => throw new NotImplementedException();

    /// <summary>R36: the custodian's movement confirmed the transfer and its Transaction is posted.</summary>
    public void Complete(int transactionId, DateTimeOffset at) => throw new NotImplementedException();
}
