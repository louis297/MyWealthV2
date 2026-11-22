using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R9, R10, R12, R13: the loader, the band checks and the <c>errors.EffectiveFrom</c> refusals.</summary>
internal static class InterestRateScheduleRules
{
    private const string EffectiveFromField = "EffectiveFrom";

    /// <summary>The current tenant's row with its bands; another tenant's or a missing id is 404.</summary>
    public static async Task<InterestRateSchedule> LoadAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid id,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = db.InterestRateSchedules.Include(schedule => schedule.BandRows)
            .Where(schedule => schedule.PublicId == id && schedule.TenantId == currentUser.TenantId);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("InterestRateSchedule", id);
    }

    /// <summary>R9 with the request field names (<c>Bands</c>, <c>Bands[i].From</c>, <c>Bands[i].RatePercent</c>).</summary>
    public static void EnsureBands(IReadOnlyList<FeeBandInput>? bands, int decimalPlaces)
    {
        var failures = TradeFeeScheduleRules.CheckBands(bands, decimalPlaces);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    public static async Task EnsureDateAsync(
        IApplicationDbContext db,
        int tenantId,
        string currency,
        DateOnly effectiveFrom,
        DateOnly cutOffDate,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        if (effectiveFrom <= cutOffDate)
        {
            throw new ValidationException([
                new ValidationFailure(EffectiveFromField,
                    $"EffectiveFrom must be after {cutOffDate:yyyy-MM-dd} (UTC): today, or the last day interest was worked out for in this currency.")
            ]);
        }

        if (await db.InterestRateSchedules.AnyAsync(
                schedule => schedule.TenantId == tenantId
                            && schedule.Currency == currency
                            && schedule.EffectiveFrom == effectiveFrom
                            && (exceptId == null || schedule.Id != exceptId),
                cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(EffectiveFromField, "A schedule for this currency already starts on this date.")
            ]);
        }
    }
}
