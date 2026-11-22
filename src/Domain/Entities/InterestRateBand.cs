namespace MyWealthV2.Domain.Entities;

/// <summary>A stored band row of an <see cref="InterestRateSchedule"/>.</summary>
public class InterestRateBand : BaseEntity
{
    private InterestRateBand()
    {
    }

    public int TenantId { get; private set; }

    public int InterestRateScheduleId { get; private set; }

    public decimal FromAmount { get; private set; }

    public decimal RatePercent { get; private set; }

    internal static InterestRateBand Create(int tenantId, decimal fromAmount, decimal ratePercent) =>
        new()
        {
            TenantId = tenantId,
            FromAmount = fromAmount,
            RatePercent = ratePercent
        };
}
