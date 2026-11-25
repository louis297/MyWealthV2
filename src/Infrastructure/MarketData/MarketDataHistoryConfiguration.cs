using System.Globalization;
using Microsoft.Extensions.Configuration;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// Reads <c>MarketDataHistory:FloorDate</c> and <c>Eodhd:HistoryDays</c> (docs/features/market-data-history.md R1, R3).
/// Throws <see cref="InvalidOperationException"/> on a bad value, so a host that calls it before <c>Build()</c> stops
/// before it listens. Reads the configuration, never the environment name.
/// </summary>
public static class MarketDataHistoryConfiguration
{
    public const string FloorDateKey = "MarketDataHistory:FloorDate";
    public const string HistoryDaysKey = "Eodhd:HistoryDays";

    public static MarketDataHistorySettings Read(IConfiguration configuration, DateOnly today)
    {
        DateOnly? floorDate = null;
        var floorText = configuration[FloorDateKey];
        if (!string.IsNullOrWhiteSpace(floorText))
        {
            if (!DateOnly.TryParseExact(floorText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                throw new InvalidOperationException($"{FloorDateKey} is not valid: it must be a date as yyyy-MM-dd.");
            }

            if (parsed > today)
            {
                throw new InvalidOperationException(
                    $"{FloorDateKey} is not valid: {parsed:yyyy-MM-dd} is after today's UTC date {today:yyyy-MM-dd}.");
            }

            floorDate = parsed;
        }

        var days = MarketDataHistorySettings.DefaultHistoryDays;
        var daysText = configuration[HistoryDaysKey];
        if (!string.IsNullOrWhiteSpace(daysText))
        {
            if (!int.TryParse(daysText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out days)
                || days is < 1 or > MarketDataHistorySettings.MaxHistoryDays)
            {
                throw new InvalidOperationException(
                    $"{HistoryDaysKey} is not valid: it must be a whole number from 1 to {MarketDataHistorySettings.MaxHistoryDays}.");
            }
        }

        return new MarketDataHistorySettings(floorDate, days);
    }
}
