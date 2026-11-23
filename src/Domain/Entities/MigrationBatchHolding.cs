namespace MyWealthV2.Domain.Entities;

/// <summary>One holding of a migration batch entry (docs/features/migration-batches.md R4): an Instrument, a quantity
/// above 0 and its cost in the instrument's quote currency.</summary>
public class MigrationBatchHolding : BaseEntity
{
    private MigrationBatchHolding()
    {
    }

    public int TenantId { get; private set; }

    public int MigrationBatchEntryId { get; private set; }

    public int InstrumentId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal Cost { get; private set; }

    internal static MigrationBatchHolding Create(int tenantId, int instrumentId, decimal quantity, decimal cost) =>
        new() { TenantId = tenantId, InstrumentId = instrumentId, Quantity = quantity, Cost = cost };
}
