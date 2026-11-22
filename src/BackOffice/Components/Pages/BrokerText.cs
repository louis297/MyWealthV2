using System.Globalization;

namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>Orders BO27, BO28: the wording of the Broker page.</summary>
public static class BrokerText
{
    /// <summary>The status filter of BO28, in the order shown.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["Received", "Processed", "Skipped"];

    /// <summary>BO27: the "Reports" column of a broker connection, worded as BO18 and BO23.</summary>
    public static string Reports(BrokerFeedState? feed) =>
        feed is null ? FeedStatus.NotReadYet : FeedStatus.Text(feed.LastSucceededAt, feed.LastFailedAt, Label(feed.LastFailure));

    public static string Label(string? failure) => failure switch
    {
        "Unreachable" => "Broker not reachable",
        "Timeout" => "Broker did not answer in time",
        "Unauthorized" => "Broker refused the key",
        "Unavailable" => "Broker unavailable",
        "BadResponse" => "Broker answer not understood",
        "InvalidItem" => "The party sent a record that cannot be read",
        _ => failure ?? "Unknown failure"
    };

    /// <summary>BO28: a kind the adapter did not know is "Unknown".</summary>
    public static string Kind(string? kind) => kind ?? "Unknown";

    /// <summary>BO28: why a report was skipped.</summary>
    public static string Reason(string? skipReason) => skipReason switch
    {
        null => "",
        "UnsupportedKind" => "Kind not supported",
        "UnknownOrder" => "No such order",
        "UnknownFill" => "No such fill",
        "Overfill" => "More than the order",
        "Repeated" => "Already known",
        _ => skipReason
    };

    /// <summary>BO28: a report skipped for an order or a fill the platform cannot place needs a person.</summary>
    public static bool NeedsPerson(ExecutionReportItem item) =>
        item.Status == "Skipped" && item.SkipReason is "UnknownOrder" or "UnknownFill" or "Overfill";

    public static string Number(decimal? value) => value?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";

    public static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
