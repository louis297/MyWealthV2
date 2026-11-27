namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The instants of a New Zealand tax year (docs/features/tax-year-events.md R3, R4). Tax year <c>Y</c> runs from
/// 00:00 on 1 April <c>Y − 1</c> to 00:00 on 1 April <c>Y</c> in New Zealand. Every 1 April since 2008 has been on
/// daylight time, 13 hours ahead of UTC, so the year starts at 11:00 UTC on 31 March <c>Y − 1</c>. A rule, the same for
/// every Tenant, and not a setting. Only the tax reads cut time this way; a day stays a UTC date everywhere else.
/// </summary>
public static class TaxYear
{
    /// <summary>R4: the first tax year whose start falls under the rule of R3.</summary>
    public const int FirstReadable = 2009;

    /// <summary>11:00 UTC on 31 March of the year before <paramref name="taxYear"/>.</summary>
    public static DateTimeOffset Start(int taxYear) => default;

    /// <summary>The start of the next tax year.</summary>
    public static DateTimeOffset End(int taxYear) => default;

    /// <summary>The tax year <paramref name="instant"/> belongs to: <c>Start(Y) ≤ instant &lt; End(Y)</c>.</summary>
    public static int Of(DateTimeOffset instant) => default;

    /// <summary>The valuation day of the opening value: 31 March of the year before <paramref name="taxYear"/>.</summary>
    public static DateOnly OpeningDay(int taxYear) => default;

    /// <summary>The valuation day of the closing value: 31 March of <paramref name="taxYear"/>.</summary>
    public static DateOnly ClosingDay(int taxYear) => default;

    /// <summary>R4: from 2009 on, and only once 31 March of the year is on or before the last final day.</summary>
    public static bool IsReadable(int taxYear, DateOnly today) => default;
}
