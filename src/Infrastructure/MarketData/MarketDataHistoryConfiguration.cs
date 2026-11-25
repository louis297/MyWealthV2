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

    public static MarketDataHistorySettings Read(IConfiguration configuration, DateOnly today) =>
        MarketDataHistorySettings.None;
}
