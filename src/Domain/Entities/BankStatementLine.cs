namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One line of a client money account's bank statement, stored once per (connection, outside id) before it is
/// used (docs/features/deposit-matching.md §5; ADR 0021 rule 7). Amount, date, payer and reference never change
/// after it is stored. A credit line moves through matching, suspense and return; a debit line is a payout, the
/// debit of a receipt return, a recall, or a line in suspense that can only be ignored (bank-returns R2–R10).
/// </summary>
public class BankStatementLine : BaseAuditableEntity
{
    public const int OutsideIdMaxLength = 100;
    public const int FreeTextMaxLength = 140;
    public const int PayerAccountMaxLength = 40;
    public const int NoteMaxLength = 200;
    public const int AmountMaxDecimalPlaces = 4;

    private BankStatementLine()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int ClientMoneyAccountId { get; private set; }

    public string Connection { get; private set; } = string.Empty;

    public string OutsideId { get; private set; } = string.Empty;

    public DateOnly ValueDate { get; private set; }

    public BankLineDirection Direction { get; private set; }

    /// <summary>Interest-run R39: a transfer or the bank's own interest; immutable, from the port.</summary>
    public StatementLineKind Kind { get; private set; }

    /// <summary>Interest-run R40: the note of a bank interest line the platform sets aside.</summary>
    public const string BankInterestNote = "Bank interest";

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string? PayerName { get; private set; }

    public string? PayerAccountMasked { get; private set; }

    public string? ReferenceText { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public BankStatementLineStatus Status { get; private set; }

    public UnmatchedReason? UnmatchedReason { get; private set; }

    public int? MatchedAccountId { get; private set; }

    public int? MatchedTransactionId { get; private set; }

    public StatementLineMatchMethod? MatchMethod { get; private set; }

    public DateTimeOffset? MatchedAt { get; private set; }

    public StatementLineAction? LastAction { get; private set; }

    public string? LastActionNote { get; private set; }

    public int? LastActionByUserId { get; private set; }

    public DateTimeOffset? LastActionAt { get; private set; }

    /// <summary>The instruction id of the platform's payment, as the bank echoed it (withdrawals R38).</summary>
    public string? EndToEndId { get; private set; }

    /// <summary>The outside id of the line this line takes back (withdrawals R38).</summary>
    public string? ReturnOfOutsideId { get; private set; }

    /// <summary>The stored line of the same connection that this line takes back (withdrawals R38).</summary>
    public int? RelatedLineId { get; private set; }

    public string? ReturnReason { get; private set; }

    /// <summary>The withdrawal whose payout or returned payout this line is (withdrawals R40, R42).</summary>
    public int? WithdrawalId { get; private set; }

    /// <summary>Trade-settlement R18–R21: the trade settlement whose cash this line is.</summary>
    public int? TradeSettlementId { get; private set; }

    /// <summary>Migration-batches R13: the migration batch this receipt funds, while it is matched to one.</summary>
    public int? MigrationBatchId { get; private set; }

    /// <summary>Dividends R24, R26: the dividend this line is the receipt of.</summary>
    public int? CorporateActionId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>A line as the bank sent it, in status <c>Received</c> (R10–R12). Payer name and reference are
    /// trimmed and cut to 140 characters; <paramref name="payerAccountMasked"/> is already masked (R11).</summary>
    public static BankStatementLine Receive(
        int tenantId,
        int clientMoneyAccountId,
        string connection,
        string outsideId,
        DateOnly valueDate,
        BankLineDirection direction,
        decimal amount,
        string currency,
        string? payerName,
        string? payerAccountMasked,
        string? referenceText,
        DateTimeOffset receivedAt,
        Guid? publicId = null,
        string? endToEndId = null,
        string? returnOfOutsideId = null,
        string? returnReason = null,
        StatementLineKind kind = StatementLineKind.Transfer)
    {
        if (!IsStorable(outsideId, direction, amount))
        {
            throw new DomainException(
                $"A statement line needs an id of 1 to {OutsideIdMaxLength} characters, a direction, and an amount above zero with at most {AmountMaxDecimalPlaces} decimal places.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency must be a three-letter code.");
        }

        if (!FitsPaymentIds(endToEndId, returnOfOutsideId))
        {
            throw new DomainException($"An end-to-end id and a returned line id are at most {OutsideIdMaxLength} characters.");
        }

        return new BankStatementLine
        {
            Kind = kind,
            PublicId = publicId ?? Guid.NewGuid(),
            TenantId = tenantId,
            ClientMoneyAccountId = clientMoneyAccountId,
            Connection = connection,
            OutsideId = outsideId,
            ValueDate = valueDate,
            Direction = direction,
            Amount = amount,
            Currency = currency.Trim().ToUpperInvariant(),
            PayerName = FreeText(payerName, FreeTextMaxLength),
            PayerAccountMasked = FreeText(payerAccountMasked, PayerAccountMaxLength),
            ReferenceText = FreeText(referenceText, FreeTextMaxLength),
            ReceivedAt = receivedAt,
            Status = BankStatementLineStatus.Received,
            EndToEndId = string.IsNullOrEmpty(endToEndId) ? null : endToEndId,
            ReturnOfOutsideId = string.IsNullOrEmpty(returnOfOutsideId) ? null : returnOfOutsideId,
            ReturnReason = FreeText(returnReason, FreeTextMaxLength)
        };
    }

    /// <summary>Withdrawals R38: an end-to-end id and a returned line id fit in 100 characters each.</summary>
    public static bool FitsPaymentIds(string? endToEndId, string? returnOfOutsideId) =>
        (endToEndId?.Length ?? 0) <= OutsideIdMaxLength && (returnOfOutsideId?.Length ?? 0) <= OutsideIdMaxLength;

    /// <summary>Withdrawals R38: the stored line this line takes back.</summary>
    public void LinkReturnedLine(int relatedLineId) => RelatedLineId = relatedLineId;

    /// <summary>True when the bank's line has a shape the platform can store (R10).</summary>
    public static bool IsStorable(string? outsideId, BankLineDirection direction, decimal amount) =>
        !string.IsNullOrWhiteSpace(outsideId)
        && outsideId.Length <= OutsideIdMaxLength
        && Enum.IsDefined(direction)
        && amount > 0
        && decimal.Round(amount, AmountMaxDecimalPlaces) == amount;

    /// <summary>Throws <see cref="StatementLineStatusException"/> unless a TenantAdmin may match it by hand now (R23).</summary>
    public void EnsureCanMatchByHand()
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Unmatched);
    }

    /// <summary>Throws <see cref="StatementLineStatusException"/> unless its match may be undone now (R23).</summary>
    public void EnsureCanUnmatch()
    {
        EnsureCredit("unmatched");
        EnsureStatus("unmatched", BankStatementLineStatus.Matched);
        if (MatchMethod is StatementLineMatchMethod.Payment or StatementLineMatchMethod.PaymentReturn)
        {
            // Withdrawals R43: a returned payout is the flow's own write; it is not undone by hand.
            throw new StatementLineStatusException("This line returned a payment and cannot be unmatched.");
        }

        if (MatchMethod == StatementLineMatchMethod.Dividend)
        {
            // Dividends R27: a receipt matched to a dividend stays; a wrong match is corrected with adjustments.
            throw new StatementLineStatusException("A receipt matched to a dividend cannot be taken out of it.");
        }

        if (MatchMethod == StatementLineMatchMethod.Settlement)
        {
            // Trade-settlement R21: a settlement line accepts no action; only a returned payment undoes it.
            throw new StatementLineStatusException("This line is the cash of a trade settlement and cannot be unmatched.");
        }
    }

    /// <summary>The platform matched it by its deposit reference (R17).</summary>
    public void MarkMatched(int accountId, int transactionId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(accountId, transactionId, StatementLineMatchMethod.Reference, at);
    }

    /// <summary>Withdrawals R40: the debit line of a withdrawal's payment; its payout is posted.</summary>
    public void MarkPayout(int accountId, int transactionId, int withdrawalId, DateTimeOffset at)
    {
        if (Direction != BankLineDirection.Debit)
        {
            throw new StatementLineStatusException("A payout is a debit line.");
        }

        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(accountId, transactionId, StatementLineMatchMethod.Payment, at);
        WithdrawalId = withdrawalId;
    }

    /// <summary>Trade-settlement R18, R19: the cash of a trade settlement, paid to or received from the broker. No
    /// Account and no Transaction: the fills were posted when they were reported.</summary>
    public void MarkSettlement(int tradeSettlementId, DateTimeOffset at)
    {
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(null, null, StatementLineMatchMethod.Settlement, at);
        TradeSettlementId = tradeSettlementId;
    }

    /// <summary>Trade-settlement R20: the credit line that returns a settlement payment's debit line. No Account and
    /// no Transaction.</summary>
    public void MarkSettlementReturn(int tradeSettlementId, int relatedLineId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(null, null, StatementLineMatchMethod.PaymentReturn, at);
        TradeSettlementId = tradeSettlementId;
        RelatedLineId = relatedLineId;
    }

    /// <summary>Withdrawals R42: the credit line that returns a payout; the payout is reversed.</summary>
    public void MarkPaymentReturn(int accountId, int transactionId, int withdrawalId, int relatedLineId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(accountId, transactionId, StatementLineMatchMethod.PaymentReturn, at);
        WithdrawalId = withdrawalId;
        RelatedLineId = relatedLineId;
    }

    /// <summary>Withdrawals R40, R42: the line names a payment but does not fit it.</summary>
    public void MarkPaymentMismatch()
    {
        EnsureStatus("unmatched", BankStatementLineStatus.Received);
        Status = BankStatementLineStatus.Unmatched;
        UnmatchedReason = Enums.UnmatchedReason.PaymentMismatch;
    }

    /// <summary>Dividends R24: the custodian's payment of a dividend, matched by its reference. No Account and no
    /// Transaction: the dividend's entitlements are posted when it is paid.</summary>
    public void MarkDividendReceipt(int corporateActionId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(null, null, StatementLineMatchMethod.Dividend, at);
        CorporateActionId = corporateActionId;
    }

    /// <summary>Dividends R24: the line names a dividend but does not fit it.</summary>
    public void MarkDividendMismatch() => MarkUnmatched(Enums.UnmatchedReason.DividendMismatch);

    /// <summary>The platform could not place it (R18).</summary>
    public void MarkUnmatched(UnmatchedReason reason)
    {
        EnsureCredit("unmatched");
        EnsureStatus("unmatched", BankStatementLineStatus.Received);
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Unmatched reason is not valid.");
        }

        Status = BankStatementLineStatus.Unmatched;
        UnmatchedReason = reason;
    }

    /// <summary>Bank-returns R2, R4, R8: the platform could not place a line of either direction.</summary>
    public void MarkUnknown(UnmatchedReason reason)
    {
        EnsureStatus("unmatched", BankStatementLineStatus.Received);
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Unmatched reason is not valid.");
        }

        Status = BankStatementLineStatus.Unmatched;
        UnmatchedReason = reason;
    }

    /// <summary>Bank-returns R6: the bank accepted the return payment.</summary>
    public void MarkReturnSent()
    {
        EnsureCredit("sent back");
        EnsureStatus("sent back", BankStatementLineStatus.ReturnRequested);
        Status = BankStatementLineStatus.ReturnSent;
    }

    /// <summary>Bank-returns R6: the bank refused the return; the line is back in suspense.</summary>
    public void MarkReturnRejected()
    {
        EnsureCredit("sent back");
        EnsureStatus("sent back", BankStatementLineStatus.ReturnRequested);
        Status = BankStatementLineStatus.Unmatched;
        UnmatchedReason = Enums.UnmatchedReason.ReturnRejected;
    }

    /// <summary>Bank-returns R3, R7: the money went back to the payer. The match, if any, is dropped.</summary>
    public void MarkReturned()
    {
        EnsureCredit("returned");
        EnsureStatus(
            "returned",
            BankStatementLineStatus.Matched,
            BankStatementLineStatus.Unmatched,
            BankStatementLineStatus.Ignored,
            BankStatementLineStatus.ReturnRequested,
            BankStatementLineStatus.ReturnSent);
        Status = BankStatementLineStatus.Returned;
        ClearMatch();
    }

    /// <summary>Bank-returns R3: the debit line of a recall, with the Reversal of the deposit when there was one.</summary>
    public void MarkRecall(int relatedLineId, int? accountId, int? transactionId, DateTimeOffset at)
    {
        EnsureDebit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        if (accountId.HasValue != transactionId.HasValue)
        {
            throw new DomainException("A recall has both an account and a Reversal, or neither.");
        }

        SetMatch(accountId, transactionId, StatementLineMatchMethod.Recall, at);
        RelatedLineId = relatedLineId;
    }

    /// <summary>Bank-returns R7: the debit line of a receipt return the bank paid.</summary>
    public void MarkReturnPaid(int relatedLineId, DateTimeOffset at)
    {
        EnsureDebit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Received);
        SetMatch(null, null, StatementLineMatchMethod.Payment, at);
        RelatedLineId = relatedLineId;
    }

    /// <summary>A TenantAdmin matched it to an account (R26); the note is optional.</summary>
    public void MatchByHand(int accountId, int transactionId, string? note, int byUserId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Unmatched);
        var trimmed = OptionalNote(note);
        SetMatch(accountId, transactionId, StatementLineMatchMethod.Manual, at);
        Record(StatementLineAction.Matched, trimmed, byUserId, at);
    }

    /// <summary>Migration-batches R13: a TenantAdmin matched the receipt to a migration batch as its funding. No Account
    /// and no Transaction: the batch's post writes the opening balances.</summary>
    public void MatchToMigrationBatch(int migrationBatchId, string? note, int byUserId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Unmatched);
        var trimmed = OptionalNote(note);
        SetMatch(null, null, StatementLineMatchMethod.Migration, at);
        MigrationBatchId = migrationBatchId;
        Record(StatementLineAction.Matched, trimmed, byUserId, at);
    }

    /// <summary>Dividends R26: a TenantAdmin accepts this receipt as the payment of a dividend, with a note. No Account
    /// and no Transaction.</summary>
    public void MatchToDividend(int corporateActionId, string note, int byUserId, DateTimeOffset at)
    {
        EnsureCredit("matched");
        EnsureStatus("matched", BankStatementLineStatus.Unmatched);
        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NoteMaxLength)
        {
            throw new DomainException($"A note is 1 to {NoteMaxLength} characters.");
        }

        SetMatch(null, null, StatementLineMatchMethod.Dividend, at);
        CorporateActionId = corporateActionId;
        Record(StatementLineAction.Matched, trimmed, byUserId, at);
    }

    /// <summary>Not a Customer's money (R27); on a debit line, a debit that is not a Customer's (bank-returns R9).</summary>
    public void Ignore(string note, int byUserId, DateTimeOffset at)
    {
        EnsureStatus("ignored", BankStatementLineStatus.Unmatched);
        var trimmed = RequiredNote(note);
        Status = BankStatementLineStatus.Ignored;
        Record(StatementLineAction.Ignored, trimmed, byUserId, at);
    }

    /// <summary>Interest-run R40: a <c>Received</c> credit line of kind <c>Interest</c> is set aside by the platform:
    /// <c>Ignored</c>, last action <c>Ignored</c>, the note <see cref="BankInterestNote"/>, no person.</summary>
    public void SetAsideAsBankInterest(DateTimeOffset at)
    {
        EnsureCredit("set aside as bank interest");
        EnsureStatus("set aside as bank interest", BankStatementLineStatus.Received);
        if (Kind != StatementLineKind.Interest)
        {
            throw new DomainException("Only a line of kind Interest is set aside as bank interest.");
        }

        Status = BankStatementLineStatus.Ignored;
        Record(StatementLineAction.Ignored, BankInterestNote, null, at);
    }

    /// <summary>Must go back to the payer (R28); the caller creates the return payment (bank-returns R5).</summary>
    public void RequestReturn(string note, int byUserId, DateTimeOffset at)
    {
        EnsureCredit("returned");
        EnsureStatus("returned", BankStatementLineStatus.Unmatched);
        var trimmed = RequiredNote(note);
        Status = BankStatementLineStatus.ReturnRequested;
        Record(StatementLineAction.ReturnRequested, trimmed, byUserId, at);
    }

    /// <summary>Back into suspense (R29), also an ignored debit line (bank-returns R9). A line to be returned is
    /// reopened only while its return payment has no send attempt (bank-returns R5).</summary>
    public void Reopen(int byUserId, DateTimeOffset at, bool returnAttempted = false)
    {
        EnsureStatus("reopened", BankStatementLineStatus.Ignored, BankStatementLineStatus.ReturnRequested);
        if (Status == BankStatementLineStatus.ReturnRequested && returnAttempted)
        {
            throw new StatementLineStatusException(
                "The bank has been asked to return this receipt, so it can no longer be reopened.");
        }

        Status = BankStatementLineStatus.Unmatched;
        Record(StatementLineAction.Reopened, null, byUserId, at);
    }

    /// <summary>The match was wrong and its deposit was reversed (R30). The reason from processing stays.</summary>
    public void Unmatch(string note, int byUserId, DateTimeOffset at)
    {
        EnsureCredit("unmatched");
        EnsureStatus("unmatched", BankStatementLineStatus.Matched);
        var trimmed = RequiredNote(note);
        Status = BankStatementLineStatus.Unmatched;
        ClearMatch();
        Record(StatementLineAction.Unmatched, trimmed, byUserId, at);
    }

    private void ClearMatch()
    {
        MatchedAccountId = null;
        MatchedTransactionId = null;
        MatchMethod = null;
        MatchedAt = null;
        MigrationBatchId = null;
        CorporateActionId = null;
    }

    private void SetMatch(int? accountId, int? transactionId, StatementLineMatchMethod method, DateTimeOffset at)
    {
        Status = BankStatementLineStatus.Matched;
        MatchedAccountId = accountId;
        MatchedTransactionId = transactionId;
        MatchMethod = method;
        MatchedAt = at;
    }

    private void Record(StatementLineAction action, string? note, int? byUserId, DateTimeOffset at)
    {
        LastAction = action;
        LastActionNote = note;
        LastActionByUserId = byUserId;
        LastActionAt = at;
    }

    private void EnsureCredit(string action)
    {
        if (Direction != BankLineDirection.Credit)
        {
            throw new StatementLineStatusException($"This line is a payment out and cannot be {action}.");
        }
    }

    private void EnsureDebit(string action)
    {
        if (Direction != BankLineDirection.Debit)
        {
            throw new StatementLineStatusException($"This line is a receipt and cannot be {action} as a payment out.");
        }
    }

    private void EnsureStatus(string action, params BankStatementLineStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new StatementLineStatusException($"This receipt is {Status} and cannot be {action}.");
        }
    }

    private static string RequiredNote(string? note) =>
        OptionalNote(note) ?? throw new DomainException($"Note must be 1 to {NoteMaxLength} characters.");

    private static string? OptionalNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= NoteMaxLength
            ? trimmed
            : throw new DomainException($"Note must be 1 to {NoteMaxLength} characters.");
    }

    private static string? FreeText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }
}
