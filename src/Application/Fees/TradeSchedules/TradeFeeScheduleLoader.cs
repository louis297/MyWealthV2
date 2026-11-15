using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Fees.TradeSchedules;

internal static class TradeFeeScheduleLoader
{
    /// <summary>
    /// The schedule by PublicId with its bands. TenantAdmin / Adviser: only the current tenant's rows; another
    /// tenant's id is 404 (R1). SystemAdmin may cross tenants.
    /// </summary>
    public static async Task<TradeFeeSchedule> LoadAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = db.TradeFeeSchedules.Include(schedule => schedule.BandRows).Where(schedule => schedule.PublicId == id);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(schedule => schedule.TenantId == currentUser.TenantId);
        }

        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("TradeFeeSchedule", id);
    }

    /// <summary>The R6 duplicate check, reported as <c>errors.EffectiveFrom</c>.</summary>
    public static Task<bool> DateTakenAsync(
        IApplicationDbContext db,
        int tenantId,
        string quoteCurrency,
        DateOnly effectiveFrom,
        int? exceptId,
        CancellationToken cancellationToken) =>
        db.TradeFeeSchedules.AnyAsync(
            schedule => schedule.TenantId == tenantId
                        && schedule.QuoteCurrency == quoteCurrency
                        && schedule.EffectiveFrom == effectiveFrom
                        && (exceptId == null || schedule.Id != exceptId),
            cancellationToken);
}
