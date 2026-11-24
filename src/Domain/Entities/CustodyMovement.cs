namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The custodian's inbound record of one change of its position in one instrument of a safekeeping account
/// (docs/features/in-kind-transfers.md R8–R19; ADR 0021 rule 7). Stored once per (connection, outside id), then
/// processed once: <c>Matched</c> to a transfer, a fill or a corporate action, or <c>Unmatched</c> with a reason.
/// A person can only ignore or reopen it.
/// </summary>
public class CustodyMovement : BaseAuditableEntity
{
    public const int OutsideIdMaxLength = 100;
    public const int SymbolMaxLength = 32;
    public const int ReferenceIdMaxLength = 100;
    public const int CounterpartyNameMaxLength = 140;
    public const int CounterpartyAccountMaxLength = 34;
    public const int ReferenceMaxLength = 140;
    public const int NoteMaxLength = 200;

    private CustodyMovement()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int SafekeepingAccountId { get; private set; }

    /// <summary>The safekeeping account's connection when the movement was read (R10).</summary>
    public string Connection { get; private set; } = string.Empty;

    public string OutsideId { get; private set; } = string.Empty;

    public CustodyMovementDirection Direction { get; private set; }

    /// <summary>Null for a kind the adapter did not know (R11).</summary>
    public CustodyMovementKind? Kind { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    /// <summary>R13: the Tenant's Instrument with this symbol and quote currency when the movement was stored.</summary>
    public int? InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public DateOnly SettlementDate { get; private set; }

    public string? InstructionId { get; private set; }

    public string? TradeReference { get; private set; }

    /// <summary>The issuer's id of the event, as the custodian sent it in <c>corporateActionId</c> (R11, R17).</summary>
    public string? CorporateActionReference { get; private set; }

    public string? CounterpartyName { get; private set; }

    public string? CounterpartyAccount { get; private set; }

    public string? Reference { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public CustodyMovementStatus Status { get; private set; }

    public CustodyMatchMethod? MatchMethod { get; private set; }

    public CustodyUnmatchedReason? UnmatchedReason { get; private set; }

    public int? AccountId { get; private set; }

    public int? TransactionId { get; private set; }

    public int? InKindTransferId { get; private set; }

    public long? OrderFillId { get; private set; }

    public int? CorporateActionId { get; private set; }

    public CustodyMovementAction? LastAction { get; private set; }

    public int? LastActionByUserId { get; private set; }

    public DateTimeOffset? LastActionAt { get; private set; }

    public string? LastActionNote { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary><c>In</c> plus, <c>Out</c> minus.</summary>
    public decimal SignedQuantity => Direction == CustodyMovementDirection.In ? Quantity : -Quantity;

    /// <summary>R11, R13: a new movement, <c>Received</c>. Free text is trimmed and cut to its length.</summary>
    public static CustodyMovement Receive(
        int tenantId,
        int safekeepingAccountId,
        string connection,
        string outsideId,
        CustodyMovementDirection direction,
        CustodyMovementKind? kind,
        string symbol,
        string currency,
        int? instrumentId,
        decimal quantity,
        DateOnly settlementDate,
        string? instructionId,
        string? tradeReference,
        string? corporateActionReference,
        string? counterpartyName,
        string? counterpartyAccount,
        string? reference,
        DateTimeOffset receivedAt,
        Guid? publicId = null)
    {
        if (quantity <= 0)
        {
            throw new DomainException("A custody movement's quantity must be greater than 0.");
        }

        var trimmedOutsideId = outsideId?.Trim() ?? string.Empty;
        if (trimmedOutsideId.Length is 0 or > OutsideIdMaxLength)
        {
            throw new DomainException($"A custody movement's outside id must be 1 to {OutsideIdMaxLength} characters.");
        }

        var trimmedSymbol = symbol?.Trim() ?? string.Empty;
        if (trimmedSymbol.Length is 0 or > SymbolMaxLength)
        {
            throw new DomainException($"A custody movement's symbol must be 1 to {SymbolMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("A custody movement's currency is required.");
        }

        return new CustodyMovement
        {
            TenantId = tenantId,
            SafekeepingAccountId = safekeepingAccountId,
            Connection = connection,
            OutsideId = trimmedOutsideId,
            Direction = direction,
            Kind = kind,
            Symbol = trimmedSymbol,
            Currency = currency.Trim().ToUpperInvariant(),
            InstrumentId = instrumentId,
            Quantity = quantity,
            SettlementDate = settlementDate,
            InstructionId = Cut(instructionId, ReferenceIdMaxLength),
            TradeReference = Cut(tradeReference, ReferenceIdMaxLength),
            CorporateActionReference = Cut(corporateActionReference, ReferenceIdMaxLength),
            CounterpartyName = Cut(counterpartyName, CounterpartyNameMaxLength),
            CounterpartyAccount = Cut(counterpartyAccount, CounterpartyAccountMaxLength),
            Reference = Cut(reference, ReferenceMaxLength),
            ReceivedAt = receivedAt,
            Status = CustodyMovementStatus.Received,
            PublicId = publicId ?? Guid.NewGuid()
        };
    }

    /// <summary>R36: the movement confirmed its transfer and posted its Transaction.</summary>
    public void MatchToTransfer(int accountId, int transactionId, int inKindTransferId)
    {
        EnsureReceived();
        Status = CustodyMovementStatus.Matched;
        MatchMethod = CustodyMatchMethod.Transfer;
        AccountId = accountId;
        TransactionId = transactionId;
        InKindTransferId = inKindTransferId;
    }

    /// <summary>R16: the movement is the delivery of a fill.</summary>
    public void MatchToFill(long orderFillId)
    {
        EnsureReceived();
        Status = CustodyMovementStatus.Matched;
        MatchMethod = CustodyMatchMethod.Trade;
        OrderFillId = orderFillId;
    }

    /// <summary>R17: the movement is the custodian's side of a corporate action.</summary>
    public void MatchToCorporateAction(int corporateActionId)
    {
        EnsureReceived();
        Status = CustodyMovementStatus.Matched;
        MatchMethod = CustodyMatchMethod.CorporateAction;
        CorporateActionId = corporateActionId;
    }

    /// <summary>R15: the movement waits in suspense with <paramref name="reason"/>.</summary>
    public void Leave(CustodyUnmatchedReason reason)
    {
        EnsureReceived();
        Status = CustodyMovementStatus.Unmatched;
        UnmatchedReason = reason;
    }

    /// <summary>R18: a person has dealt with a movement in suspense. It keeps its reason.</summary>
    public void Ignore(int byUserId, DateTimeOffset at, string note)
    {
        EnsureStatus(CustodyMovementStatus.Unmatched, "ignored");
        var trimmed = Note(note) ?? throw new DomainException("Note is required.");
        Status = CustodyMovementStatus.Ignored;
        Record(CustodyMovementAction.Ignore, byUserId, at, trimmed);
    }

    /// <summary>R18: an ignored movement is in suspense again.</summary>
    public void Reopen(int byUserId, DateTimeOffset at, string? note)
    {
        EnsureStatus(CustodyMovementStatus.Ignored, "reopened");
        var trimmed = Note(note);
        Status = CustodyMovementStatus.Unmatched;
        Record(CustodyMovementAction.Reopen, byUserId, at, trimmed);
    }

    private void Record(CustodyMovementAction action, int byUserId, DateTimeOffset at, string? note)
    {
        LastAction = action;
        LastActionByUserId = byUserId;
        LastActionAt = at;
        LastActionNote = note;
    }

    private void EnsureReceived()
    {
        if (Status != CustodyMovementStatus.Received)
        {
            throw new CustodyMovementStatusException($"This movement is {Status} and has already been processed.");
        }
    }

    private void EnsureStatus(CustodyMovementStatus from, string action)
    {
        if (Status != from)
        {
            throw new CustodyMovementStatusException($"This movement is {Status} and cannot be {action}.");
        }
    }

    private static string? Note(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > NoteMaxLength)
        {
            throw new DomainException($"Note must be at most {NoteMaxLength} characters.");
        }

        return trimmed;
    }

    private static string? Cut(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }
}
