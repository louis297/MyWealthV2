namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One line of a client money account's bank statement, stored once per (connection, outside id) before it is
/// used (docs/features/deposit-matching.md §5; ADR 0021 rule 7). Amount, date, payer and reference never change
/// after it is stored. Only a credit line moves through matching and suspense; a debit line waits (R14).
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
        Guid? publicId = null) => throw new NotImplementedException();

    /// <summary>True when the bank's line has a shape the platform can store (R10).</summary>
    public static bool IsStorable(string? outsideId, BankLineDirection direction, decimal amount) =>
        throw new NotImplementedException();

    /// <summary>The platform matched it by its deposit reference (R17).</summary>
    public void MarkMatched(int accountId, int transactionId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>The platform could not place it (R18).</summary>
    public void MarkUnmatched(UnmatchedReason reason) => throw new NotImplementedException();

    /// <summary>A TenantAdmin matched it to an account (R26).</summary>
    public void MatchByHand(int accountId, int transactionId, string? note, int byUserId, DateTimeOffset at) =>
        throw new NotImplementedException();

    /// <summary>Not a Customer's money (R27).</summary>
    public void Ignore(string note, int byUserId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>Must go back to the payer (R28).</summary>
    public void RequestReturn(string note, int byUserId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>Back into suspense (R29).</summary>
    public void Reopen(int byUserId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>The match was wrong; its deposit was reversed (R30).</summary>
    public void Unmatch(string note, int byUserId, DateTimeOffset at) => throw new NotImplementedException();
}
