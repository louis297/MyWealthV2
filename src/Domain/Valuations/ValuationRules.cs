namespace MyWealthV2.Domain.Valuations;

/// <summary>The age limit of a price or a rate in a valuation (docs/features/account-valuation.md R11).</summary>
public static class ValuationRules
{
    /// <summary>The most calendar days a price or rate row may be older than the day, the limit of orders R10 and
    /// fx-conversions R10.</summary>
    public const int MaxAgeDays = 4;

    /// <summary>True when <paramref name="day"/> minus <paramref name="asOfDate"/> is at most
    /// <see cref="MaxAgeDays"/>.</summary>
    public static bool IsFresh(DateOnly asOfDate, DateOnly day) => day.DayNumber - asOfDate.DayNumber <= MaxAgeDays;

    /// <summary>Valuation-run R4: the latest day a stored valuation may be made for on <paramref name="today"/> (UTC).</summary>
    public static DateOnly LastFinalDay(DateOnly today) => throw new NotImplementedException();
}
