namespace MyWealthV2.Domain.Enums;

/// <summary>Which feed of a market data connection a <c>MarketDataFeed</c> row describes
/// (docs/features/simulated-instruments.md §5, §6.1).</summary>
public enum MarketDataFeedKind
{
    Prices = 0,

    /// <summary>Corporate action announcements, read after a cursor (corporate-actions R7).</summary>
    CorporateActions = 1
}
