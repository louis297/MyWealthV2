namespace MyWealthV2.Domain.Enums;

/// <summary>Which of the four totals of a migration batch an issue is about (migration-batches R5, R9).</summary>
public enum MigrationTotal
{
    AccountCount = 0,
    Cash = 1,
    Quantity = 2,
    Cost = 3
}
