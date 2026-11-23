namespace MyWealthV2.Domain.Exceptions;

/// <summary>The migration batch is not in a status its move starts from, or the four-eyes rule refuses the move
/// (docs/features/migration-batches.md R6, R13, R14, R17–R20).</summary>
public class MigrationBatchStatusException(string message) : DomainException(message);
