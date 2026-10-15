using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports;

public static class AdviserReportScope
{
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        int tenantId,
        Guid adviserId,
        UserRole role,
        Guid? callerPublicId,
        CancellationToken cancellationToken)
    {
        var adviser = await db.Users.AsNoTracking()
                          .SingleOrDefaultAsync(
                              row => row.PublicId == adviserId
                                     && row.TenantId == tenantId
                                     && row.Role == UserRole.Adviser,
                              cancellationToken)
                      ?? throw new NotFoundException("Adviser", adviserId);

        if (role == UserRole.Adviser && callerPublicId != adviser.PublicId)
        {
            throw new NotFoundException("Adviser", adviser.PublicId);
        }
    }
}
