using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.BankReconciliations;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.BankReconciliations;

/// <summary>A daily run in which the reconciliation of at least one client money account failed (R16).</summary>
public sealed class BankReconciliationFailedException(IReadOnlyList<Guid> clientMoneyAccounts)
    : Exception($"Bank reconciliation failed for client money accounts {string.Join(", ", clientMoneyAccounts)}.");

/// <summary>
/// One run of <c>bank-reconciliation</c> (bank-reconciliation R16, R18): every active client money account of every
/// Tenant, also a disabled one, in <c>Id</c> order, each in its own scope and transactions. It reads stored data only,
/// so it also runs while the bank is down. A failed account is logged and does not stop the others; the run then fails
/// once every account was tried. No automatic retries: the next day, or "Run now", is the retry.
/// </summary>
public sealed class BankReconciliationJob(
    IApplicationDbContext db,
    IServiceScopeFactory scopes,
    ILogger<BankReconciliationJob> logger)
{
    public const string JobId = "bank-reconciliation";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(CancellationToken cancellationToken)
    {
        var accounts = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
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
                await scope.ServiceProvider.GetRequiredService<IReconcileClientMoneyAccount>()
                    .RunAsync(account.Id, ReconciliationTrigger.Scheduled, ranByUserId: null, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Bank reconciliation of client money account {ClientMoneyAccountId} failed.",
                    account.PublicId);
                failed.Add(account.PublicId);
            }
        }

        if (failed.Count > 0)
        {
            throw new BankReconciliationFailedException(failed);
        }
    }
}

/// <summary>R16: daily at 17:00 UTC on queue <c>bank</c>, before the business day in New Zealand.</summary>
public static class BankReconciliationRecurringJob
{
    public const string Cron = "0 17 * * *";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<BankReconciliationJob>(
            BankReconciliationJob.JobId,
            BankFeed.BankStatementRecurringJob.Queue,
            job => job.Run(CancellationToken.None),
            Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
