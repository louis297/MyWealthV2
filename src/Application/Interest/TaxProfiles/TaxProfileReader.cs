using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Users;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Interest.TaxProfiles;

/// <summary>Interest-run R1, R5: the Customer in the caller's scope, and the item of its profile.</summary>
internal static class TaxProfileReader
{
    /// <summary>The Customer for a TenantAdmin (the tenant) or the assigned Adviser; else 404.</summary>
    public static async Task<User> CustomerAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var assignedAdviserId = CustomerScope.AssignedAdviserInternalId(currentUser);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(
                   row => row.PublicId == customerId
                          && row.Role == UserRole.Customer
                          && row.TenantId == currentUser.TenantId
                          && (assignedAdviserId == null || row.AdviserId == assignedAdviserId),
                   cancellationToken)
               ?? throw new NotFoundException("Customer", customerId);
    }

    /// <summary>R8 for one Customer; <paramref name="withUpdatedBy"/> is false for the Customer's own read.</summary>
    public static async Task<TaxProfileDto> ItemAsync(
        IApplicationDbContext db,
        int customerId,
        bool withUpdatedBy,
        CancellationToken cancellationToken)
    {
        var profile = await db.CustomerTaxProfiles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == customerId, cancellationToken);
        TaxProfileUpdatedByDto? updatedBy = null;
        if (profile is not null && withUpdatedBy)
        {
            updatedBy = await db.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(user => user.Id == profile.UpdatedByUserId)
                .Select(user => new TaxProfileUpdatedByDto(user.PublicId, user.Name))
                .SingleOrDefaultAsync(cancellationToken);
        }

        return TaxProfileDto.From(profile, updatedBy);
    }
}
