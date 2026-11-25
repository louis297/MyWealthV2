using System.Globalization;

namespace MyWealthV2.Application.MarketData;

/// <summary>The <c>from</c> and <c>to</c> of the market data read routes (market-data-history R21): <c>yyyy-MM-dd</c>,
/// both inclusive.</summary>
public static class MarketDataDates
{
    public const string FormatMessage = "must be a date as yyyy-MM-dd.";

    /// <summary>True when <paramref name="text"/> is empty or a <c>yyyy-MM-dd</c> date.</summary>
    public static bool IsEmptyOrDate(string? text) => string.IsNullOrEmpty(text) || TryParse(text, out _);

    public static DateOnly? Parse(string? text) => TryParse(text, out var date) ? date : null;

    /// <summary>True when both dates are given and <c>from</c> is after <c>to</c>.</summary>
    public static bool FromIsAfterTo(string? from, string? to) =>
        Parse(from) is { } first && Parse(to) is { } last && first > last;

    private static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
