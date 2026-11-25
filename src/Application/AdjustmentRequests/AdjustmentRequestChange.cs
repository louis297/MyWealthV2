using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Finds, guards and saves an adjustment request (adjustment-review R7).</summary>
internal static class AdjustmentRequestChange
{
    public static async Task<Tenant> TenantAsync(IApplicationDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }

    public static int ActorId(ICurrentUser currentUser) => currentUser.Person?.Id ?? throw new ForbiddenAccessException();

    /// <summary>R1, R7: the request of the caller's Tenant (else 404) at the <c>rowVersion</c> the caller read (else 409).</summary>
    public static async Task<AdjustmentRequest> FindAsync(
        IApplicationDbContext db, ICurrentUser currentUser, Guid id, string? rowVersion, CancellationToken cancellationToken)
    {
        var request = await AdjustmentRequestReader.Scope(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken)
                      ?? throw new NotFoundException("Adjustment request", id);
        if (!string.Equals(Convert.ToBase64String(request.RowVersion ?? []), rowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        return request;
    }

    /// <summary>R7, R8: a wrong status or the requester deciding is 400 at <c>Status</c>.</summary>
    public static void Guard(Action change)
    {
        try
        {
            change();
        }
        catch (AdjustmentRequestStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
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
