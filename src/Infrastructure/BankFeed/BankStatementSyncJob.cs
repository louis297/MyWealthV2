using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>A run in which the read of at least one client money account failed (deposit-matching R7).</summary>
public sealed class BankStatementSyncFailedException(string connection, IReadOnlyList<Guid> clientMoneyAccounts)
    : Exception($"Bank statement read failed for connection {connection}: client money accounts {string.Join(", ", clientMoneyAccounts)}.");

/// <summary>
/// One run of <c>bank-statement-sync:{connection}</c> (deposit-matching R6, R7, R13): pull the statements, then
/// process the stored lines. One run of a connection at a time; no automatic retries, the schedule is the retry
/// (ADR 0021 rule 15).
/// </summary>
public sealed class BankStatementSyncJob(
    PullBankStatements pull,
    ProcessStatementLines process,
    SqlRunLock runLock,
    ILogger<BankStatementSyncJob> logger)
{
    public static string JobId(string connection) => $"bank-statement-sync:{connection}";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string connection, CancellationToken cancellationToken)
    {
        await using var held = await runLock.TryAcquireAsync(JobId(connection), cancellationToken);
        if (held is null)
        {
            logger.LogInformation("Another run of {JobId} is still going; this run does nothing.", JobId(connection));
            return;
        }

        var outcome = await pull.RunAsync(connection, cancellationToken);

        // A failed read does not stop the processing of lines already stored (R13).
        await process.RunAsync(connection, cancellationToken);

        if (outcome.AnyFailed)
        {
            throw new BankStatementSyncFailedException(connection, outcome.FailedClientMoneyAccounts);
        }
    }
}

/// <summary>Registers one recurring job per configured connection, on queue <c>bank</c> (deposit-matching R6).</summary>
public static class BankStatementRecurringJob
{
    public const string Queue = "bank";

    private const string Prefix = "bank-statement-sync:";

    /// <summary>Every <paramref name="pollMinutes"/> minutes from the hour, UTC; 60 is once an hour.</summary>
    public static string Cron(int pollMinutes) => pollMinutes == 60 ? "0 * * * *" : $"*/{pollMinutes} * * * *";

    /// <summary>Adds or updates the job of every configured connection and removes the job of a connection that is
    /// no longer configured.</summary>
    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredBankConnections connections)
    {
        foreach (var connection in connections.Settings)
        {
            var name = connection.Name;
            recurring.AddOrUpdate<BankStatementSyncJob>(
                BankStatementSyncJob.JobId(name),
                Queue,
                job => job.Run(name, CancellationToken.None),
                Cron(connection.PollMinutes),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        var configured = connections.Settings.Select(entry => BankStatementSyncJob.JobId(entry.Name)).ToHashSet(StringComparer.Ordinal);
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
