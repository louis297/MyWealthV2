namespace MyWealthV2.Domain.Valuations;

/// <summary>One currency of an FX fold: its total, the rate row it used (none for the reporting currency, or when no
/// row was found) and its value in the reporting currency, which only a <see cref="FoldLineStatus.Folded"/> line has.</summary>
public sealed record FoldLine(string Currency, decimal Total, FoldLineStatus Status, StoredRate? Fx, decimal? Value);

/// <summary>The per-currency totals of one day folded into the reporting currency (valuation-history R10, R11).</summary>
public sealed record FoldedTotal(string Currency, IReadOnlyList<FoldLine> Lines, decimal Total, bool Complete);

/// <summary>The FX fold of one day (valuation-history R10, R11). Reads no clock and no store.</summary>
public static class FxFold
{
    /// <param name="totals">One total per currency.</param>
    /// <param name="reportingCurrency">The currency every other total is folded into.</param>
    /// <param name="decimalPlaces">The decimal places of the reporting currency.</param>
    /// <param name="rates">The stored row of the directed pair from each other currency into the reporting currency,
    /// as of <paramref name="day"/>; a currency with no entry has no row.</param>
    /// <param name="day">The day the totals are of.</param>
    public static FoldedTotal Compute(
        IReadOnlyDictionary<string, decimal> totals,
        string reportingCurrency,
        int decimalPlaces,
        IReadOnlyDictionary<string, StoredRate> rates,
        DateOnly day) =>
        new(reportingCurrency, [], 0m, false);
}
