using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.MigrationBatches;

/// <summary>What the migration batch commands share: the lookup in the caller's Tenant (R1), the row version
/// (R6, R19) and the 400 at <c>errors.Status</c>.</summary>
internal static class MigrationBatchRules
{
    public static IQueryable<MigrationBatch> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        return db.MigrationBatches.Where(batch => batch.TenantId == tenantId);
    }

    public static IQueryable<MigrationBatch> WithBody(IQueryable<MigrationBatch> query) =>
        query.Include(batch => batch.Entries).ThenInclude(entry => entry.Holdings)
            .Include(batch => batch.ControlTotalRows);

    public static async Task<MigrationBatch> FindAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        string? rowVersion,
        CancellationToken cancellationToken,
        bool withBody = false)
    {
        var query = Scope(db, currentUser);
        var batch = await (withBody ? WithBody(query) : query)
                        .SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken)
                    ?? throw new NotFoundException("MigrationBatch", id);
        if (!string.Equals(Convert.ToBase64String(batch.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        return batch;
    }

    public static ValidationException StatusError(string message) => new([new ValidationFailure("Status", message)]);

    /// <summary>Runs a move of the batch and answers a status refusal at <c>errors.Status</c>.</summary>
    public static void Guard(Action move)
    {
        try
        {
            move();
        }
        catch (MigrationBatchStatusException exception)
        {
            throw StatusError(exception.Message);
        }
    }

    public static async Task SaveAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
