namespace MyWealthV2.Domain.Orders;

/// <summary>
/// The fee terms of an order (orders R11): the trade fee schedule fixed when it was placed, or none, with the decimal
/// places of the quote currency. Every fill of the order is charged from it (R33) and the reservation of a buy counts
/// it (R12, R39).
/// </summary>
public sealed record OrderFeeTerms(TradeFeeSchedule? Schedule, int DecimalPlaces)
{
    /// <summary>An order with no schedule: no fee.</summary>
    public static OrderFeeTerms None { get; } = new(null, 2);

    /// <summary>The schedule's fee on <paramref name="gross"/> (trade-fees R8); 0 with no schedule.</summary>
    public decimal FeeFor(decimal gross) => Schedule?.FeeFor(gross, DecimalPlaces).Fee ?? 0m;
}
