using Hangfire;
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
    public Task Run(string connection, CancellationToken cancellationToken) =>
        throw new NotImplementedException(pull.GetType().Name + process.GetType().Name + runLock.GetType().Name + logger.GetType().Name);
}

/// <summary>Registers one recurring job per configured connection, on queue <c>bank</c> (deposit-matching R6).</summary>
public static class BankStatementRecurringJob
{
    public const string Queue = "bank";

    public static string Cron(int pollMinutes) => throw new NotImplementedException();

    public static void Register(IRecurringJobManager recurring, JobStorage storage, ConfiguredBankConnections connections) =>
        throw new NotImplementedException();
}
