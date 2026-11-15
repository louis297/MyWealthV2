using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>
/// Trade fees R7: the tenant's schedule for the quote currency with the latest <c>effectiveFrom</c> on or before the
/// trade's UTC date. None → the trade has no fee.
/// </summary>
public static class ApplicableTradeFeeSchedule
{
    public static DateOnly DateOf(DateTimeOffset bookedAt) => DateOnly.FromDateTime(bookedAt.UtcDateTime);

    public static Task<TradeFeeSchedule?> ForAsync(
        IApplicationDbContext db,
        int tenantId,
        string quoteCurrency,
        DateOnly tradeDate,
        CancellationToken cancellationToken) =>
        db.TradeFeeSchedules
            .Include(schedule => schedule.BandRows)
            .Where(schedule => schedule.TenantId == tenantId
                               && schedule.QuoteCurrency == quoteCurrency
                               && schedule.EffectiveFrom <= tradeDate)
            .OrderByDescending(schedule => schedule.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
}
