namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>Simulated instruments BO23, corporate actions BO24: the "Prices" and "Corporate actions" columns of a
/// market data connection, worded as BO18.</summary>
public static class MarketDataStatus
{
    public static string Text(MarketDataFeed? feed) =>
        feed is null ? FeedStatus.NotReadYet : FeedStatus.Text(feed.LastSucceededAt, feed.LastFailedAt, Label(feed.LastFailure));

    public static string Label(string? failure) => failure switch
    {
        "Unreachable" => "Market data source not reachable",
        "Timeout" => "Market data source did not answer in time",
        "Unauthorized" => "Market data source refused the key",
        "Unavailable" => "Market data source unavailable",
        "BadResponse" => "Market data answer not understood",
        "InvalidItem" => "The party sent a record that cannot be read",
        _ => failure ?? "Unknown failure"
    };
}
