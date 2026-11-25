namespace MyWealthV2.Application.MarketData;

/// <summary>
/// The history settings of <c>worker</c> (docs/features/market-data-history.md R1, R3): the history floor date
/// (<c>MarketDataHistory:FloorDate</c>; null when not set, so no history is read) and how far back EODHD is asked
/// (<c>Eodhd:HistoryDays</c>, default 360).
/// </summary>
public sealed record MarketDataHistorySettings(DateOnly? FloorDate, int HistoryDays = MarketDataHistorySettings.DefaultHistoryDays)
{
    public const int DefaultHistoryDays = 360;
    public const int MaxHistoryDays = 36500;

    /// <summary>No floor date: every source is asked for the latest value only.</summary>
    public static MarketDataHistorySettings None { get; } = new((DateOnly?)null);
}
