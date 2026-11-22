using MyWealthV2.Application.Fees.TradeSchedules;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R15, §8.1: the rate schedule item.</summary>
public sealed record InterestRateScheduleDto(
    Guid Id,
    string Currency,
    DateOnly EffectiveFrom,
    IReadOnlyList<FeeBandDto> Bands,
    bool InEffect,
    string RowVersion)
{
    public static InterestRateScheduleDto From(InterestRateSchedule schedule, DateOnly cutOffDate) =>
        new(
            schedule.PublicId,
            schedule.Currency,
            schedule.EffectiveFrom,
            schedule.Bands.Select(band => new FeeBandDto(band.From, band.RatePercent)).ToList(),
            schedule.IsInEffect(cutOffDate),
            Convert.ToBase64String(schedule.RowVersion ?? []));
}

public sealed record CreatedInterestRateScheduleDto(Guid Id);
