namespace MyWealthV2.Domain.MarketData;

/// <summary>
/// The final rule of a stored price or rate row (docs/features/market-data-history.md R4, R5): a row of
/// <c>InstrumentPrices</c> or <c>FxRates</c> is final when its <c>AsOfDate</c> is two or more days before the
/// platform's UTC date. A final row is never updated; a missing one may still be inserted.
/// </summary>
public static class MarketDataRow
{
    public static bool IsFinal(DateOnly asOfDate, DateOnly today) => asOfDate <= today.AddDays(-2);
}
