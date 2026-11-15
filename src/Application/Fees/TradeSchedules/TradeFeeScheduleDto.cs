using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>Schedule item (trade-fees §8.1).</summary>
public sealed record TradeFeeScheduleDto(
    Guid Id,
    string QuoteCurrency,
    DateOnly EffectiveFrom,
    decimal Minimum,
    decimal? Cap,
    IReadOnlyList<FeeBandDto> Bands,
    bool InEffect,
    string RowVersion)
{
    public static TradeFeeScheduleDto From(TradeFeeSchedule schedule, DateOnly cutOffDate) =>
        new(
            schedule.PublicId,
            schedule.QuoteCurrency,
            schedule.EffectiveFrom,
            schedule.Minimum,
            schedule.Cap,
            schedule.Bands.Select(band => new FeeBandDto(band.From, band.RatePercent)).ToList(),
            schedule.IsInEffect(cutOffDate),
            Convert.ToBase64String(schedule.RowVersion ?? []));
}

public sealed record FeeBandDto(decimal From, decimal RatePercent);
