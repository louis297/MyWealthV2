namespace MyWealthV2.Domain.Entities;

/// <summary>One stored row of a migration batch's control totals (docs/features/migration-batches.md R5, §6): cash or
/// cost in a currency, or the quantity of an Instrument.</summary>
public class MigrationBatchControlTotal : BaseEntity
{
    private MigrationBatchControlTotal()
    {
    }

    public int TenantId { get; private set; }

    public int MigrationBatchId { get; private set; }

    public MigrationControlTotalKind Kind { get; private set; }

    /// <summary>Set for <c>Cash</c> and <c>Cost</c>, null for <c>Quantity</c>.</summary>
    public string? Currency { get; private set; }

    /// <summary>Set for <c>Quantity</c>, null for <c>Cash</c> and <c>Cost</c>.</summary>
    public int? InstrumentId { get; private set; }

    public decimal Amount { get; private set; }

    internal static MigrationBatchControlTotal Create(
        int tenantId,
        MigrationControlTotalKind kind,
        string? currency,
        int? instrumentId,
        decimal amount) =>
        new() { TenantId = tenantId, Kind = kind, Currency = currency, InstrumentId = instrumentId, Amount = amount };
}
