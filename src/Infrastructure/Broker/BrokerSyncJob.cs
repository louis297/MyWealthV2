using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Broker;
using MyWealthV2.Infrastructure.BankFeed;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>A run in which a step failed or left work for the next run (docs/features/orders.md R19).</summary>
public sealed class BrokerSyncFailedException(string connection, IReadOnlyList<string> steps)
    : Exception($"Broker sync failed for connection {connection}: {string.Join("; ", steps)}.");

/// <summary>
/// One run of <c>broker-sync:{connection}</c> (orders R19): send the orders, send the cancel requests, pull the
/// execution reports, process them, post the fills that wait, in this order, so an order and its fill can meet in
/// one run. A failed step is recorded and the run goes on with the next step on what is already stored; a run in
/// which any step failed or left work is reported to Hangfire as failed after every step was tried. One run of a
/// connection at a time; no automatic retries, the schedule is the retry (ADR 0021 rule 15).
/// </summary>
public sealed class BrokerSyncJob(
    SendBrokerOrders send,
    PullExecutionReports pull,
    ProcessExecutionReports process,
    PostOrderFills post,
    SqlRunLock runLock,
    ILogger<BrokerSyncJob> logger)
{
    public static string JobId(string connection) => $"broker-sync:{connection}";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string connection, CancellationToken cancellationToken)
    {
        await using var held = await runLock.TryAcquireAsync(JobId(connection), cancellationToken);
        if (held is null)
        {
            logger.LogInformation("Another run of {JobId} is still going; this run does nothing.", JobId(connection));
            return;
        }

        var failed = new List<string>();
        await StepAsync("send", failed, async () =>
        {
            var outcome = await send.SendAsync(connection, cancellationToken);
            return outcome.Failed > 0 ? $"{outcome.Failed} order(s) not sent" : null;
        }, cancellationToken);
        await StepAsync("cancel", failed, async () =>
        {
            var outcome = await send.CancelAsync(connection, cancellationToken);
            return outcome.Failed > 0 ? $"{outcome.Failed} cancel request(s) not answered" : null;
        }, cancellationToken);
        await StepAsync("pull", failed, async () =>
        {
            var outcome = await pull.RunAsync(connection, cancellationToken);
            return outcome.Failure is { } failure ? failure.ToString() : null;
        }, cancellationToken);
        await StepAsync("process", failed, async () =>
        {
            var outcome = await process.RunAsync(connection, cancellationToken);
            return outcome.Failed > 0 ? $"{outcome.Failed} report(s) left" : null;
        }, cancellationToken);
        await StepAsync("post", failed, async () =>
        {
            var outcome = await post.RunAsync(connection, cancellationToken);
            return outcome.Failed + outcome.Waiting > 0 ? $"{outcome.Failed + outcome.Waiting} fill(s) not posted" : null;
        }, cancellationToken);

        if (failed.Count > 0)
        {
            // R19: reported to Hangfire as failed; the next scheduled run is the retry.
            throw new BrokerSyncFailedException(connection, failed);
        }
    }

    /// <summary>Runs one step. Its failure never stops the next step.</summary>
    private async Task StepAsync(string name, List<string> failed, Func<Task<string?>> step, CancellationToken cancellationToken)
    {
        try
        {
            if (await step() is { } problem)
            {
                failed.Add($"{name}: {problem}");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Step {Step} of the broker sync failed; the run goes on.", name);
            failed.Add($"{name}: {exception.GetType().Name}");
        }
    }
}

/// <summary>Registers one recurring job per configured connection, on queue <c>broker</c> (orders R19).</summary>
public static class BrokerSyncRecurringJob
{
    public const string Queue = "broker";

    private const string Prefix = "broker-sync:";

    /// <summary>Every <paramref name="pollMinutes"/> minutes from the hour, UTC; 60 is once an hour.</summary>
    public static string Cron(int pollMinutes) => BankStatementRecurringJob.Cron(pollMinutes);

    /// <summary>Adds or updates the job of every configured connection and removes the job of a connection that is
    /// no longer configured.</summary>
    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredBrokerConnections connections)
    {
        foreach (var connection in connections.Settings)
        {
            var name = connection.Name;
            recurring.AddOrUpdate<BrokerSyncJob>(
                BrokerSyncJob.JobId(name),
                Queue,
                job => job.Run(name, CancellationToken.None),
                Cron(connection.PollMinutes),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        var configured = connections.Settings.Select(entry => BrokerSyncJob.JobId(entry.Name)).ToHashSet(StringComparer.Ordinal);
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
