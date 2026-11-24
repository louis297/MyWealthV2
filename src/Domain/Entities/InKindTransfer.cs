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
    public bool IsOpen => IsOpenStatus(Status);

    /// <summary>R26, R43: the quantity of an open transfer <c>Out</c>, else 0.</summary>
    public decimal ReservedQuantity => Direction == TransferDirection.Out && IsOpen ? Quantity : 0m;

    public static bool IsOpenStatus(InKindTransferStatus status) =>
        status is InKindTransferStatus.Requested or InKindTransferStatus.Approved or InKindTransferStatus.Sent;

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
        Guid? publicId = null)
    {
        if (quantity <= 0 || decimal.Round(quantity, 8) != quantity)
        {
            throw new DomainException("Quantity must be greater than 0, with at most 8 decimal places.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency is required.");
        }

        if (direction == TransferDirection.In)
        {
            if (cost is null)
            {
                throw new DomainException("Cost is required for a transfer in.");
            }

            if (cost < 0 || decimal.Round(cost.Value, 4) != cost.Value)
            {
                throw new DomainException("Cost must be 0 or more, with at most 4 decimal places.");
            }
        }
        else if (cost is not null || acquiredOn is not null)
        {
            throw new DomainException("Cost is given only for a transfer in.");
        }

        if (!ClientMoneyAccount.IsValidAccountNumber(counterpartyAccount))
        {
            throw new DomainException(
                $"Counterparty account must be 1 to {ClientMoneyAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        }

        return new InKindTransfer
        {
            TenantId = tenantId,
            AccountId = accountId,
            InstrumentId = instrumentId,
            Direction = direction,
            Quantity = quantity,
            Currency = currency.Trim().ToUpperInvariant(),
            Cost = cost,
            AcquiredOn = acquiredOn,
            ChangesBeneficialOwner = changesBeneficialOwner,
            CounterpartyName = Required(counterpartyName, CounterpartyNameMaxLength, "Counterparty name"),
            CounterpartyAccount = counterpartyAccount.Trim(),
            CounterpartyHolderName = Required(counterpartyHolderName, CounterpartyHolderNameMaxLength, "Counterparty holder name"),
            Memo = Optional(memo, MemoMaxLength, "Memo"),
            Status = InKindTransferStatus.Requested,
            RequestedByUserId = requestedByUserId,
            RequestedAt = requestedAt,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    /// <summary>R31: a TenantAdmin other than the requester approves it; the safekeeping account is kept.</summary>
    public void Approve(int byUserId, DateTimeOffset at, string? note, int safekeepingAccountId)
    {
        EnsureStatus("approved", InKindTransferStatus.Requested);
        EnsureNotRequester(byUserId);
        Decide(InKindTransferDecision.Approve, byUserId, at, Optional(note, NoteMaxLength, "Note"));
        SafekeepingAccountId = safekeepingAccountId;
        Status = InKindTransferStatus.Approved;
    }

    /// <summary>R31: a TenantAdmin other than the requester refuses it, with a note. The reservation ends.</summary>
    public void Reject(int byUserId, DateTimeOffset at, string note)
    {
        EnsureStatus("rejected", InKindTransferStatus.Requested);
        EnsureNotRequester(byUserId);
        var trimmed = Optional(note, NoteMaxLength, "Note") ?? throw new DomainException("Note is required.");
        Decide(InKindTransferDecision.Reject, byUserId, at, trimmed);
        Status = InKindTransferStatus.Rejected;
    }

    /// <summary>R32: withdrawn before the custodian was asked. The reservation ends.</summary>
    public void Cancel(int byUserId, DateTimeOffset at, string? note)
    {
        EnsureStatus("cancelled", InKindTransferStatus.Requested, InKindTransferStatus.Approved);
        if (Attempts > 0)
        {
            throw new InKindTransferStatusException(CancelAfterAttemptMessage);
        }

        Decide(InKindTransferDecision.Cancel, byUserId, at, Optional(note, NoteMaxLength, "Note"));
        Status = InKindTransferStatus.Cancelled;
    }

    /// <summary>R34: an attempt to send is recorded and committed before the call.</summary>
    public void RecordAttempt(DateTimeOffset at)
    {
        EnsureStatus("sent", InKindTransferStatus.Approved);
        Attempts++;
        LastAttemptAt = at;
    }

    /// <summary>R35: the custodian accepted the instruction. A transfer a movement already completed keeps its status.</summary>
    public void Accept(string custodianTransferId, DateTimeOffset at)
    {
        var id = custodianTransferId?.Trim() ?? string.Empty;
        if (id.Length is 0 or > CustodianTransferIdMaxLength)
        {
            throw new DomainException($"The custodian's transfer id must be 1 to {CustodianTransferIdMaxLength} characters.");
        }

        CustodianTransferId = id;
        AcceptedAt ??= at;
        LastFailure = null;
        if (Status == InKindTransferStatus.Approved)
        {
            Status = InKindTransferStatus.Sent;
        }
    }

    /// <summary>R35: the custodian refused the instruction. The reservation ends.</summary>
    public void Fail(TransferRejectReason reason, DateTimeOffset at)
    {
        EnsureStatus("failed", InKindTransferStatus.Approved);
        FailureReason = reason;
        Status = InKindTransferStatus.Failed;
    }

    /// <summary>R35: the call failed; the transfer stays <c>Approved</c> and is sent again.</summary>
    public void RecordFailure(CustodianFailure failure) => LastFailure = failure;

    /// <summary>R33: a TenantAdmin ends a transfer the custodian was asked to make. The reservation ends.</summary>
    public void SetAside(int byUserId, DateTimeOffset at, string note)
    {
        EnsureAsked("set aside");
        var trimmed = Optional(note, NoteMaxLength, "Note") ?? throw new DomainException("Note is required.");
        SetAsideByUserId = byUserId;
        SetAsideAt = at;
        SetAsideNote = trimmed;
        Status = InKindTransferStatus.SetAside;
    }

    /// <summary>R36: the custodian's movement confirmed the transfer and its Transaction is posted.</summary>
    public void Complete(int transactionId, DateTimeOffset at)
    {
        EnsureAsked("completed");
        TransactionId = transactionId;
        CompletedAt = at;
        Status = InKindTransferStatus.Completed;
    }

    /// <summary>R33, R36: <c>Sent</c>, or <c>Approved</c> with a send attempt.</summary>
    public bool WasAsked => Status == InKindTransferStatus.Sent || (Status == InKindTransferStatus.Approved && Attempts > 0);

    private void EnsureAsked(string action)
    {
        if (!WasAsked)
        {
            throw new InKindTransferStatusException($"This transfer is {Status} and cannot be {action}.");
        }
    }

    private void Decide(InKindTransferDecision action, int byUserId, DateTimeOffset at, string? note)
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
            throw new InKindTransferStatusException(FourEyesMessage);
        }
    }

    private void EnsureStatus(string action, params InKindTransferStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new InKindTransferStatusException($"This transfer is {Status} and cannot be {action}.");
        }
    }

    private static string Required(string value, int maxLength, string label) =>
        Optional(value, maxLength, label) ?? throw new DomainException($"{label} must be 1 to {maxLength} characters.");

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
