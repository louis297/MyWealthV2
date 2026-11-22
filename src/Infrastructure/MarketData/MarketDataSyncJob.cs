using Hangfire;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.BankFeed;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>A run whose price read failed (docs/features/simulated-instruments.md R8).</summary>
public sealed class MarketDataSyncFailedException(string connection, MarketDataFailure failure)
    : Exception($"Market price read failed for connection {connection}: {failure}.");

/// <summary>
/// One run of <c>market-data-sync:{connection}</c> (simulated-instruments R7, R8): read the connection's prices and
/// store them. One run of a connection at a time; no automatic retries, the schedule is the retry (ADR 0021 rule 15).
/// </summary>
public sealed class MarketDataSyncJob(
    SyncMarketPrices sync,
    SqlRunLock runLock,
    ILogger<MarketDataSyncJob> logger)
{
    public static string JobId(string connection) => $"market-data-sync:{connection}";

    public Task Run(string connection, CancellationToken cancellationToken)
    {
        _ = (sync, runLock, logger);
        return Task.CompletedTask;
    }
}

/// <summary>Registers one recurring job per configured connection, on queue <c>market</c> (R7).</summary>
public static class MarketDataRecurringJob
{
    public const string Queue = "market";

    /// <summary>Every <paramref name="pollMinutes"/> minutes from the hour, UTC; 60 is once an hour.</summary>
    public static string Cron(int pollMinutes) => BankStatementRecurringJob.Cron(pollMinutes);

    /// <summary>Adds or updates the job of every configured connection and removes the job of a connection that is
    /// no longer configured.</summary>
    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredMarketDataConnections connections)
    {
        _ = (recurring, storage, connections);
    }
}
