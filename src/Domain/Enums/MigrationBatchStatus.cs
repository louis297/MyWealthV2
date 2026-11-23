namespace MyWealthV2.Domain.Enums;

/// <summary>Where a migration batch is (docs/features/migration-batches.md §4.2).</summary>
public enum MigrationBatchStatus
{
    /// <summary>Being prepared; its body and funding can change.</summary>
    Draft = 0,

    /// <summary>Valid when it was submitted; waits for a second person.</summary>
    Submitted = 1,

    /// <summary>Every entry is in the ledger. Final.</summary>
    Posted = 2,

    /// <summary>Ended without posting; its funding lines are back in suspense. Final.</summary>
    Cancelled = 3
}
