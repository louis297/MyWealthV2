namespace MyWealthV2.Domain.Entities;

/// <summary>One entry of a migration batch (docs/features/migration-batches.md R4, R23): one Account with its opening
/// cash, holdings or both, in the place it has in the body, and the Transactions the post wrote for it.</summary>
public class MigrationBatchEntry : BaseEntity
{
    private readonly List<MigrationBatchHolding> _holdings = [];

    private MigrationBatchEntry()
    {
    }

    public int TenantId { get; private set; }

    public int MigrationBatchId { get; private set; }

    /// <summary>The place of the entry in the body, from 0 (R29: entries are read in the order of the body).</summary>
    public int Position { get; private set; }

    public int AccountId { get; private set; }

    /// <summary>The account's number at the previous provider, or null.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Opening cash in the Account's currency, or null when the entry has holdings only.</summary>
    public decimal? Cash { get; private set; }

    public IReadOnlyList<MigrationBatchHolding> Holdings => _holdings;

    /// <summary>The <c>Opening</c> the post wrote for the entry's cash (R23).</summary>
    public int? OpeningTransactionId { get; private set; }

    /// <summary>The <c>OpeningHoldings</c> the post wrote for the entry's holdings (R23).</summary>
    public int? OpeningHoldingsTransactionId { get; private set; }

    internal static MigrationBatchEntry Create(
        int tenantId,
        int position,
        int accountId,
        string? externalReference,
        decimal? cash,
        IEnumerable<MigrationBatchHolding> holdings)
    {
        var entry = new MigrationBatchEntry
        {
            TenantId = tenantId,
            Position = position,
            AccountId = accountId,
            ExternalReference = externalReference,
            Cash = cash
        };
        entry._holdings.AddRange(holdings);
        return entry;
    }

    /// <summary>R23: stores the Transactions the post wrote for this entry, once.</summary>
    public void RecordTransactions(int? openingTransactionId, int? openingHoldingsTransactionId)
    {
        if (OpeningTransactionId is not null || OpeningHoldingsTransactionId is not null)
        {
            throw new DomainException("The Transactions of this entry are already recorded.");
        }

        if (openingTransactionId is null && openingHoldingsTransactionId is null)
        {
            throw new DomainException("An entry is posted with at least one Transaction.");
        }

        OpeningTransactionId = openingTransactionId;
        OpeningHoldingsTransactionId = openingHoldingsTransactionId;
    }
}
