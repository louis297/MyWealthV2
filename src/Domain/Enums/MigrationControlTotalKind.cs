namespace MyWealthV2.Domain.Enums;

/// <summary>The kind of one stored control total row of a migration batch (migration-batches R5, §6). The account
/// count is a column of the batch, not a row.</summary>
public enum MigrationControlTotalKind
{
    /// <summary>Cash in one currency.</summary>
    Cash = 0,

    /// <summary>Quantity of one Instrument.</summary>
    Quantity = 1,

    /// <summary>Cost in one currency.</summary>
    Cost = 2
}
