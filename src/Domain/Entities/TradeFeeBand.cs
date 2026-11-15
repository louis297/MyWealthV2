namespace MyWealthV2.Domain.Entities;

/// <summary>A stored band row of a <see cref="TradeFeeSchedule"/>.</summary>
public class TradeFeeBand : BaseEntity
{
    private TradeFeeBand()
    {
    }

    public int TenantId { get; private set; }

    public int TradeFeeScheduleId { get; private set; }

    public decimal FromAmount { get; private set; }

    public decimal RatePercent { get; private set; }

    internal static TradeFeeBand Create(int tenantId, decimal fromAmount, decimal ratePercent) =>
        new()
        {
            TenantId = tenantId,
            FromAmount = fromAmount,
            RatePercent = ratePercent
        };
}
