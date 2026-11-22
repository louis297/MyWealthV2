using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.BankFeed;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>A run that failed (docs/features/simulated-instruments.md R8, corporate-actions R8, R25): the announcement
/// pull or the price read failed, or an item was left for the next run.</summary>
public sealed class MarketDataSyncFailedException(string connection, string reason)
    : Exception($"Market data run failed for connection {connection}: {reason}.");

/// <summary>
/// One run of <c>market-data-sync:{connection}</c> (simulated-instruments R7, R8; corporate-actions R8): pull, process and
/// apply the connection's corporate actions, then read and store its prices. One run of a connection at a time; no
/// automatic retries, the schedule is the retry (ADR 0021 rule 15).
/// </summary>
public sealed class MarketDataSyncJob(
    SyncMarketDataConnection sync,
    SqlRunLock runLock,
    ILogger<MarketDataSyncJob> logger)
{
    public static string JobId(string connection) => $"market-data-sync:{connection}";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string connection, CancellationToken cancellationToken)
    {
        await using var held = await runLock.TryAcquireAsync(JobId(connection), cancellationToken);
        if (held is null)
        {
            logger.LogInformation("Another run of {JobId} is still going; this run does nothing.", JobId(connection));
            return;
        }

        var outcome = await sync.RunAsync(connection, cancellationToken);
        if (outcome.Failed)
        {
            // R8: reported to Hangfire as failed; the next scheduled run is the retry.
            var reason = outcome.Pull.Failure is { } pullFailure
                ? $"corporate action read {pullFailure}, price read skipped"
                : outcome.Prices?.Failure is { } priceFailure
                    ? $"price read {priceFailure}"
                    : "corporate action items left for the next run";
            throw new MarketDataSyncFailedException(connection, reason);
        }
    }
}

/// <summary>Registers one recurring job per configured connection, on queue <c>market</c> (R7).</summary>
public static class MarketDataRecurringJob
{
    public const string Queue = "market";

    private const string Prefix = "market-data-sync:";

    /// <summary>Every <paramref name="pollMinutes"/> minutes from the hour, UTC; 60 is once an hour.</summary>
    public static string Cron(int pollMinutes) => BankStatementRecurringJob.Cron(pollMinutes);

    /// <summary>Adds or updates the job of every configured connection and removes the job of a connection that is
    /// no longer configured.</summary>
    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredMarketDataConnections connections)
    {
        foreach (var connection in connections.Settings)
        {
            var name = connection.Name;
            recurring.AddOrUpdate<MarketDataSyncJob>(
                MarketDataSyncJob.JobId(name),
                Queue,
                job => job.Run(name, CancellationToken.None),
                Cron(connection.PollMinutes),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        var configured = connections.Settings.Select(entry => MarketDataSyncJob.JobId(entry.Name)).ToHashSet(StringComparer.Ordinal);
        using var storageConnection = storage.GetConnection();
        foreach (var job in storageConnection.GetRecurringJobs())
        {
            if (job.Id.StartsWith(Prefix, StringComparison.Ordinal) && !configured.Contains(job.Id))
            {
                recurring.RemoveIfExists(job.Id);
            }
        }
    }
}
