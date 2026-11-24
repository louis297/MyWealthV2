namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>In-kind transfers BO30, BO32: the "Feed" column of a safekeeping account, worded as BO18 and BO23.</summary>
public static class CustodianText
{
    public static string Feed(CustodianFeedState? feed) =>
        feed is null ? FeedStatus.NotReadYet : FeedStatus.Text(feed.LastSucceededAt, feed.LastFailedAt, Label(feed.LastFailure));

    public static string PositionsAt(CustodianFeedState? feed) =>
        feed?.PositionsAt is { } at
            ? at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)
            : "—";

    public static string Label(string? failure) => failure switch
    {
        "Unreachable" => "Custodian not reachable",
        "Timeout" => "Custodian did not answer in time",
        "Unauthorized" => "Custodian refused the key",
        "Unavailable" => "Custodian unavailable",
        "UnknownAccount" => "The custodian does not know this account",
        "BadResponse" => "Custodian answer not understood",
        "InvalidItem" => "The party sent a record that cannot be read",
        _ => failure ?? "Unknown failure"
    };
}
