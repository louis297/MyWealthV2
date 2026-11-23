namespace MyWealthV2.Domain.Migrations;

/// <summary>The header of a migration batch (migration-batches R3).</summary>
public sealed record MigrationBatchHeader(string Name, string PreviousProvider, DateOnly AsOfDate);

/// <summary>One entry of a migration batch as the body gives it (R4): an Account with its opening cash, holdings or both.</summary>
public sealed record MigrationEntrySpec(
    int AccountId,
    string? ExternalReference,
    decimal? Cash,
    IReadOnlyList<MigrationHoldingSpec> Holdings);

/// <summary>One holding of an entry (R4): quantity above 0 and cost in the instrument's quote currency.</summary>
public sealed record MigrationHoldingSpec(int InstrumentId, decimal Quantity, decimal Cost);

/// <summary>An amount in one currency: a cash or cost total, or a funding total (R5, R8).</summary>
public sealed record MigrationCurrencyAmount(string Currency, decimal Amount);

/// <summary>A quantity of one Instrument (R5, R8).</summary>
public sealed record MigrationInstrumentQuantity(int InstrumentId, decimal Quantity);

/// <summary>The four figures of a migration batch (R5, R8): the number of accounts, the cash per currency, the
/// quantity per Instrument and the cost per currency. Typed by a person as control totals, or worked out from the
/// entries as computed totals.</summary>
public sealed record MigrationControlTotals(
    int AccountCount,
    IReadOnlyList<MigrationCurrencyAmount> Cash,
    IReadOnlyList<MigrationInstrumentQuantity> Quantity,
    IReadOnlyList<MigrationCurrencyAmount> Cost)
{
    public static MigrationControlTotals Empty { get; } = new(0, [], [], []);
}

/// <summary>What a migrated Transaction records about where its balance came from (R22): the previous provider and
/// the date its statement was taken.</summary>
public sealed record MigrationOrigin(DateOnly AsOfDate, string PreviousProvider);
