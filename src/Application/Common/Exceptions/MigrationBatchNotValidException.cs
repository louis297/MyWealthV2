namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>Migration batches R17, R21: a batch with validation issues is not submitted or posted
/// (api-design §4.2 <c>migration_batch_not_valid</c>). The caller reads the issues on the item.</summary>
public sealed class MigrationBatchNotValidException(int issueCount)
    : ProblemException(ProblemKind.Rule, "migration_batch_not_valid", "Migration batch not valid",
        $"The migration batch has {issueCount} issues.");
