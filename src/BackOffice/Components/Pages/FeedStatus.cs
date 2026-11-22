using System.Globalization;

namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>Deposit matching BO18: the "Statement" column of a client money account.</summary>
public static class FeedStatus
{
    public const string NotReadYet = "Not read yet";

    public static string Text(ClientMoneyAccountFeed? feed) =>
        feed is null ? NotReadYet : Text(feed.LastSucceededAt, feed.LastFailedAt, Label(feed.LastFailure));

    /// <summary>The wording of BO18, shared with the market data feed (simulated-instruments BO23).</summary>
    public static string Text(DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, string failureLabel)
    {
        if (lastFailedAt is { } failedAt && (lastSucceededAt is not { } succeeded || failedAt > succeeded))
        {
            return $"Failing since {Instant(failedAt)}: {failureLabel}";
        }

        return lastSucceededAt is { } at ? $"Read {Instant(at)}" : NotReadYet;
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
