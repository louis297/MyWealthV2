using System.Globalization;

namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>Deposit matching BO18: the "Statement" column of a client money account.</summary>
public static class FeedStatus
{
    public static string Text(ClientMoneyAccountFeed? feed)
    {
        if (feed is null)
        {
            return "Not read yet";
        }

        if (feed.LastFailedAt is { } failedAt && (feed.LastSucceededAt is not { } succeeded || failedAt > succeeded))
        {
            return $"Failing since {Instant(failedAt)}: {Label(feed.LastFailure)}";
        }

        return feed.LastSucceededAt is { } at ? $"Read {Instant(at)}" : "Not read yet";
    }

    public static string Label(string? failure) => failure switch
    {
        "Unreachable" => "Bank not reachable",
        "Timeout" => "Bank did not answer in time",
        "Unauthorized" => "Bank refused the key",
        "Unavailable" => "Bank unavailable",
        "UnknownAccount" => "Bank does not know this account",
        "BadResponse" => "Bank answer not understood",
        "InvalidLine" => "Statement line rejected",
        _ => failure ?? "Unknown failure"
    };

    private static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
