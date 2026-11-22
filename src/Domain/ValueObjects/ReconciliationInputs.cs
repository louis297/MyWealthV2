namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// What a bank reconciliation run read at one instant, in one SNAPSHOT transaction (bank-reconciliation R6–R10, R14):
/// the bank side of client money account <see cref="ClientMoneyAccountId"/>, the Custodial cash of its Tenant in its
/// currency, and what the run lists between them.
/// </summary>
public sealed record ReconciliationInputs
{
    public required int TenantId { get; init; }

    public required int ClientMoneyAccountId { get; init; }

    public required string Currency { get; init; }

    /// <summary>The stored statement balance (R3, R7); null when the cursor has none (R5, R12).</summary>
    public decimal? StatementBalance { get; init; }

    public DateTimeOffset? BalanceAt { get; init; }

    public decimal? OpeningBalance { get; init; }

    /// <summary>The signed sum of every statement line stored for the account, whatever its status (R7).</summary>
    public decimal StatementLinesTotal { get; init; }

    /// <summary>The account's lines in a status that makes an item (R9); other lines may be passed and are skipped.</summary>
    public IReadOnlyList<ReconciliationLine> Lines { get; init; } = [];

    /// <summary>Every cash leg on every Custodial account of the Tenant in the currency (R8).</summary>
    public decimal CustodialCash { get; init; }

    /// <summary>Those legs, summed per group (R10), without the legs of interest postings.</summary>
    public IReadOnlyList<ReconciliationLedgerCash> Ledger { get; init; } = [];

    /// <summary>Interest-run R41: the <c>Principal</c> legs of Transactions with source <c>InterestRun</c>, and how many
    /// Transactions.</summary>
    public decimal InterestCredited { get; init; }

    public int InterestCreditedCount { get; init; }

    /// <summary>Interest-run R41: their <c>Tax</c> legs (negative), and how many Transactions have one.</summary>
    public decimal TaxWithheld { get; init; }

    public int TaxWithheldCount { get; init; }

    public decimal ReservedCash { get; init; }

    public int PaymentsInFlightCount { get; init; }

    public decimal PaymentsInFlightAmount { get; init; }

    public DateTimeOffset? FeedLastSucceededAt { get; init; }

    public DateTimeOffset? FeedLastFailedAt { get; init; }

    public BankFeedFailure? FeedLastFailure { get; init; }
}

/// <summary>A statement line as it was at the run (R9, R21). <see cref="Amount"/> is positive, as stored.</summary>
public sealed record ReconciliationLine(
    int Id,
    BankLineDirection Direction,
    DateOnly ValueDate,
    decimal Amount,
    BankStatementLineStatus Status,
    UnmatchedReason? UnmatchedReason,
    string? Note,
    StatementLineKind Kind = StatementLineKind.Transfer);

/// <summary>
/// The cash legs of the Transactions of one <see cref="Type"/> (with the <see cref="OriginalType"/> of a Reversal)
/// whose <c>BankFeed</c> or <c>Payment</c> source belongs to client money account <see cref="ThroughClientMoneyAccountId"/>,
/// or to none (R10). <see cref="Count"/> is the number of Transactions.
/// </summary>
public sealed record ReconciliationLedgerCash(
    TransactionType Type,
    TransactionType? OriginalType,
    int? ThroughClientMoneyAccountId,
    decimal Sum,
    int Count);
