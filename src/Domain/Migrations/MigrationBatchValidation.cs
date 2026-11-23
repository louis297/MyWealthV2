namespace MyWealthV2.Domain.Migrations;

/// <summary>The state of an entry's Account that the issues read (migration-batches R9, R10).</summary>
public sealed record MigrationAccountState(
    int AccountId,
    string Name,
    CustodyMode CustodyMode,
    AccountStatus Status,
    bool CustomerDisabled,
    bool HasTransactions,
    string Currency,
    int CurrencyDecimalPlaces);

/// <summary>The state of a holding's Instrument that the issues read (R9).</summary>
public sealed record MigrationInstrumentState(int InstrumentId, string Symbol, bool IsActive, string QuoteCurrency);

/// <summary>What <see cref="MigrationBatchValidation.Compute"/> reads: the batch's control totals and entries, the
/// state of their Accounts and Instruments, and the amount and currency of each funding line.</summary>
public sealed record MigrationValidationInputs(
    MigrationControlTotals ControlTotals,
    IReadOnlyList<MigrationEntrySpec> Entries,
    IReadOnlyDictionary<int, MigrationAccountState> Accounts,
    IReadOnlyDictionary<int, MigrationInstrumentState> Instruments,
    IReadOnlyList<MigrationCurrencyAmount> Funding);

/// <summary>One reason a batch is not valid (R9), with what it is about.</summary>
public sealed record MigrationIssue(
    MigrationIssueCode Code,
    int? AccountId,
    int? InstrumentId,
    string? Currency,
    MigrationTotal? Total,
    string Message);

/// <summary>The computed totals, the funding totals and the issues of a batch (R8, R9).</summary>
public sealed record MigrationValidationResult(
    MigrationControlTotals ComputedTotals,
    IReadOnlyList<MigrationCurrencyAmount> FundingTotals,
    IReadOnlyList<MigrationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Migration batches R8, R9: a pure function over a batch and the state of its Accounts, Instruments and funding lines.
/// It decides the read, the submit and the post (R11).
/// </summary>
public static class MigrationBatchValidation
{
    public static MigrationValidationResult Compute(MigrationValidationInputs inputs) => throw new NotImplementedException();
}
