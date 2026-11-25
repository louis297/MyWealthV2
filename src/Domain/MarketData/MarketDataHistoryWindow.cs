namespace MyWealthV2.Domain.MarketData;

/// <summary>
/// The dates a history read asks a source for (docs/features/market-data-history.md R2, R3): from the later of the
/// floor date and today minus <c>days</c> (no days: the floor date) up to today.
/// </summary>
public sealed record MarketDataHistoryWindow
{
    public MarketDataHistoryWindow(DateOnly floorDate, DateOnly today, int? days = null)
    {
        Today = today;
    }

    /// <summary>The first date asked for.</summary>
    public DateOnly From { get; }

    public DateOnly Today { get; }

    public bool Contains(DateOnly date) => false;
}
