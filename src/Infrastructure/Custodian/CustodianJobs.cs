using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Custodian;
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
    public Task Run(string connection, CancellationToken cancellationToken)
    {
        _ = (send, pull, process, runLock, logger);
        throw new NotImplementedException();
    }
}

/// <summary>Registers one recurring job per configured custodian connection, on queue <c>custodian</c> (R9).</summary>
public static class CustodianSyncRecurringJob
{
    public const string Queue = "custodian";

    public static string Cron(int pollMinutes) => throw new NotImplementedException();

    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredCustodianConnections connections) =>
        throw new NotImplementedException();
}

/// <summary>A daily run in which the reconciliation of at least one safekeeping account failed (R57).</summary>
public sealed class CustodianReconciliationFailedException(IReadOnlyList<Guid> safekeepingAccounts)
    : Exception($"Custodian reconciliation failed for safekeeping accounts {string.Join(", ", safekeepingAccounts)}.");

/// <summary>One run of <c>custodian-reconciliation</c> (in-kind-transfers R57).</summary>
public sealed class CustodianReconciliationJob(
    IApplicationDbContext db,
    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes,
    ILogger<CustodianReconciliationJob> logger)
{
    public const string JobId = "custodian-reconciliation";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task Run(CancellationToken cancellationToken)
    {
        _ = (db, scopes, logger);
        throw new NotImplementedException();
    }
}

/// <summary>R57: daily at 17:10 UTC on queue <c>custodian</c>, after the bank reconciliation.</summary>
public static class CustodianReconciliationRecurringJob
{
    public const string Cron = "10 17 * * *";

    public static void Register(IRecurringJobManager recurring) => throw new NotImplementedException();
}
