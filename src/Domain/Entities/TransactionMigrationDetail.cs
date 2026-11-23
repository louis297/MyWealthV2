namespace MyWealthV2.Domain.Entities;

/// <summary>
/// Where a migrated opening balance came from (docs/features/migration-batches.md R22, R31): the date the previous
/// provider's statement was taken and the provider's name. Exactly one on a Transaction a migration batch wrote and
/// on its Reversal; immutable.
/// </summary>
public class TransactionMigrationDetail
{
    private TransactionMigrationDetail()
    {
    }

    public int TransactionId { get; private set; }

    public int TenantId { get; private set; }

    public DateOnly AsOfDate { get; private set; }

    public string PreviousProvider { get; private set; } = string.Empty;

    internal static TransactionMigrationDetail Create(int tenantId, Migrations.MigrationOrigin origin) =>
        throw new NotImplementedException();

    /// <summary>A new row with the same values, for a Reversal.</summary>
    internal TransactionMigrationDetail Copy() => throw new NotImplementedException();
}
