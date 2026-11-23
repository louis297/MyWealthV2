using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MigrationBatches;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>
/// Migration batches R13, R14: a receipt matched to a migration batch is its funding. Nothing is posted; the line and
/// the batch row change in one database transaction, so the batch's <c>RowVersion</c> moves with its funding.
/// </summary>
internal static class MigrationBatchFunding
{
    public static async Task MatchAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        Guid lineId,
        Guid migrationBatchId,
        string? rowVersion,
        string? note,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role is not { } role || !RolePermissions.Has(role, Policies.MigrationBatchesManage))
        {
            throw new ForbiddenAccessException();
        }

        var line = await StatementLineReader.FindAsync(db, currentUser, lineId, cancellationToken);
        StatementLineActions.CheckRowVersion(line, rowVersion);
        StatementLineActions.Guard(line.EnsureCanMatchByHand);
        var actor = StatementLineActions.ActorId(currentUser);
        var batch = await MigrationBatchRules.Scope(db, currentUser)
                        .SingleOrDefaultAsync(row => row.PublicId == migrationBatchId, cancellationToken)
                    ?? throw new NotFoundException("MigrationBatch", migrationBatchId);
        var now = time.GetUtcNow();
        try
        {
            batch.AddFunding(now);
        }
        catch (MigrationBatchStatusException exception)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(MatchStatementLineCommand.MigrationBatchId), exception.Message)
            ]);
        }

        line.MatchToMigrationBatch(batch.Id, note, actor, now);
        await StatementLineActions.SaveAsync(db, cancellationToken);
    }

    public static async Task UnmatchAsync(
        IApplicationDbContext db,
        BankStatementLine line,
        string note,
        int actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var batch = await db.MigrationBatches.SingleAsync(row => row.Id == line.MigrationBatchId, cancellationToken);
        MigrationBatchRules.Guard(() => batch.RemoveFunding(now));
        line.Unmatch(note, actor, now);
        await StatementLineActions.SaveAsync(db, cancellationToken);
    }
}
