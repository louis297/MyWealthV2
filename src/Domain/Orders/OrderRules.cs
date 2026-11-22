namespace MyWealthV2.Domain.Orders;

/// <summary>
/// Orders R10, R12: the price limit the platform puts on every order, the age a reference price may have, and the
/// most an order can cost before fees. The caller never chooses a limit.
/// </summary>
public static class OrderRules
{
    /// <summary>The collar: a buy may fill up to this much above the reference price, a sell this much below.</summary>
    public const decimal CollarPercent = 5m;

    /// <summary>An order needs a reference price dated at most this many calendar days before the day it is placed.</summary>
    public const int ReferencePriceMaxAgeDays = 4;

    /// <summary>R10: <c>Round(referencePrice × 1.05, 8)</c> for a buy, <c>Round(referencePrice × 0.95, 8)</c> for a
    /// sell: the highest price the broker may buy at, the lowest it may sell at.</summary>
    public static decimal PriceLimit(OrderSide side, decimal referencePrice)
    {
        var factor = side == OrderSide.Buy ? 100m + CollarPercent : 100m - CollarPercent;
        return decimal.Round(referencePrice * factor / 100m, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>R12: <c>Round(quantity × priceLimit, 4)</c>.</summary>
    public static decimal MaximumGross(decimal quantity, decimal priceLimit) =>
        decimal.Round(quantity * priceLimit, 4, MidpointRounding.AwayFromZero);

    /// <summary>R10: the price is dated at most <see cref="ReferencePriceMaxAgeDays"/> days before <paramref name="placedOn"/>.</summary>
    public static bool IsReferencePriceFresh(DateOnly priceDate, DateOnly placedOn) =>
        placedOn.DayNumber - priceDate.DayNumber <= ReferencePriceMaxAgeDays;
}
