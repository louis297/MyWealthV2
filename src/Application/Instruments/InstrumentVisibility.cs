using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Instruments;

/// <summary>The instruments a caller may read by id (instruments.md; market-data-history R19): a SystemAdmin any
/// Tenant's, everyone else the current Tenant's only.</summary>
public static class InstrumentVisibility
{
    public static IQueryable<Instrument> Readable(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var query = db.Instruments.AsNoTracking();
        return currentUser.Role == UserRole.SystemAdmin
            ? query
            : query.Where(instrument => instrument.TenantId == currentUser.TenantId);
    }
}
