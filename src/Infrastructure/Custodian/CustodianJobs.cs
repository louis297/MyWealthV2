using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.CustodianReconciliations;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.BankFeed;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>A run in which a step or the read of at least one safekeeping account failed (in-kind-transfers R9).</summary>
public sealed class CustodianSyncFailedException(string connection, string detail)
    : Exception($"Custodian sync failed for connection {connection}: {detail}.");

/// <summary>
/// One run of <c>custodian-sync:{connection}</c> (in-kind-transfers R9): send the approved transfers, pull every active
/// safekeeping account of the connection, then process the stored movements. One run of a connection at a time; no
/// automatic retries, the schedule is the retry (ADR 0021 rule 15).
/// </summary>
public sealed class CustodianSyncJob(
    SendInKindTransfers send,
    PullCustodyMovements pull,
    ProcessCustodyMovements process,
    SqlRunLock runLock,
    ILogger<CustodianSyncJob> logger)
{
    public static string JobId(string connection) => $"custodian-sync:{connection}";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(string connection, CancellationToken cancellationToken)
    {
        await using var held = await runLock.TryAcquireAsync(JobId(connection), cancellationToken);
        if (held is null)
        {
            logger.LogInformation("Another run of {JobId} is still going; this run does nothing.", JobId(connection));
            return;
        }

        // R9: a failed step is recorded and the run goes on with what is already stored.
        var failures = new List<string>();
        await StepAsync("send", () => send.RunAsync(connection, cancellationToken), failures);
        CustodianPullOutcome? outcome = null;
        await StepAsync("pull", async () => outcome = await pull.RunAsync(connection, cancellationToken), failures);
        await StepAsync("process", () => process.RunAsync(connection, cancellationToken), failures);
        if (outcome is { AnyFailed: true })
        {
            failures.Add($"safekeeping accounts {string.Join(", ", outcome.FailedSafekeepingAccounts)}");
        }

        if (failures.Count > 0)
        {
            throw new CustodianSyncFailedException(connection, string.Join("; ", failures));
        }
    }

    private async Task StepAsync(string step, Func<Task> run, List<string> failures)
    {
        try
        {
            await run();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The {Step} step of a custodian sync failed.", step);
            failures.Add($"the {step} step");
        }
    }
}

/// <summary>Registers one recurring job per configured custodian connection, on queue <c>custodian</c> (R9).</summary>
public static class CustodianSyncRecurringJob
{
    public const string Queue = "custodian";

    private const string Prefix = "custodian-sync:";

    /// <summary>Every <paramref name="pollMinutes"/> minutes from the hour, UTC; 60 is once an hour.</summary>
    public static string Cron(int pollMinutes) => pollMinutes == 60 ? "0 * * * *" : $"*/{pollMinutes} * * * *";

    /// <summary>Adds or updates the job of every configured connection and removes the job of a connection that is no
    /// longer configured.</summary>
    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredCustodianConnections connections)
    {
        foreach (var connection in connections.Settings)
        {
            var name = connection.Name;
            recurring.AddOrUpdate<CustodianSyncJob>(
                CustodianSyncJob.JobId(name),
                Queue,
                job => job.Run(name, CancellationToken.None),
                Cron(connection.PollMinutes),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        var configured = connections.Settings.Select(entry => CustodianSyncJob.JobId(entry.Name)).ToHashSet(StringComparer.Ordinal);
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

/// <summary>A daily run in which the reconciliation of at least one safekeeping account failed (R57).</summary>
public sealed class CustodianReconciliationFailedException(IReadOnlyList<Guid> safekeepingAccounts)
    : Exception($"Custodian reconciliation failed for safekeeping accounts {string.Join(", ", safekeepingAccounts)}.");

/// <summary>One run of <c>custodian-reconciliation</c> (in-kind-transfers R57).</summary>
public sealed class CustodianReconciliationJob(
    IApplicationDbContext db,
    IServiceScopeFactory scopes,
    ILogger<CustodianReconciliationJob> logger)
{
    public const string JobId = "custodian-reconciliation";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(CancellationToken cancellationToken)
    {
        // R57: every active safekeeping account of every Tenant, also a disabled one, in Id order.
        var accounts = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.PublicId })
            .ToListAsync(cancellationToken);

        var failed = new List<Guid>();
        foreach (var account in accounts)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReconcileSafekeepingAccount>()
                    .RunAsync(account.Id, ReconciliationTrigger.Scheduled, ranByUserId: null, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Custodian reconciliation of safekeeping account {SafekeepingAccountId} failed.", account.PublicId);
                failed.Add(account.PublicId);
            }
        }

        if (failed.Count > 0)
        {
            throw new CustodianReconciliationFailedException(failed);
        }
    }
}

/// <summary>R57: daily at 17:10 UTC on queue <c>custodian</c>, after the bank reconciliation.</summary>
public static class CustodianReconciliationRecurringJob
{
    public const string Cron = "10 17 * * *";

    public static void Register(IRecurringJobManager recurring) =>
        recurring.AddOrUpdate<CustodianReconciliationJob>(
            CustodianReconciliationJob.JobId,
            CustodianSyncRecurringJob.Queue,
            job => job.Run(CancellationToken.None),
            Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
