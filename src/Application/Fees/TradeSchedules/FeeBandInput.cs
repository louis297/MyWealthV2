namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>One band in a request body: <c>{ from, ratePercent }</c> (trade-fees §8.1).</summary>
public sealed class FeeBandInput
{
    public decimal From { get; init; }

    public decimal RatePercent { get; init; }
}
