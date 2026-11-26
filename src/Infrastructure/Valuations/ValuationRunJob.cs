using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Valuations.Runs;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Infrastructure.Valuations;

/// <summary>A daily valuation job in which the run of at least one Tenant, or at least one account, failed
/// (valuation-run R14).</summary>
public sealed class ValuationRunFailedException(IReadOnlyList<Guid> tenants)
    : Exception($"The valuation run failed for tenants {string.Join(", ", tenants)}.");

/// <summary>
/// One execution of <c>valuation-run</c> (valuation-run R14): a run with trigger <c>Scheduled</c> through the last final
/// day for every Tenant that has at least one Custodial account, in <c>Id</c> order, a disabled Tenant included, each in
/// its own scope. A failed Tenant is logged and does not stop the others; the job then fails once every Tenant was
/// tried. No automatic retries: the next day's run takes up what is left (R6).
/// </summary>
public sealed class ValuationRunJob(
    IApplicationDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ValuationRunJob> logger)
{
    public const string JobId = "valuation-run";

    public const string Queue = "valuation";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(CancellationToken cancellationToken)
    {
        var throughDate = ValuationRules.LastFinalDay(LedgerDay.Of(time.GetUtcNow()));
        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(tenant => db.Accounts.IgnoreQueryFilters()
                .Any(account => account.TenantId == tenant.Id && account.CustodyMode == CustodyMode.Custodial))
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => new { tenant.Id, tenant.PublicId })
            .ToListAsync(cancellationToken);

        var failed = new List<Guid>();
        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var run = await scope.ServiceProvider.GetRequiredService<IRunValuations>()
                    .RunAsync(tenant.Id, throughDate, ValuationRunTrigger.Scheduled, ranByUserId: null, cancellationToken);
                if (run.AccountsFailed > 0)
                {
                    logger.LogError("Valuation run {RunId} of tenant {TenantId} failed on {Count} accounts.",
                        run.PublicId, tenant.PublicId, run.AccountsFailed);
                    failed.Add(tenant.PublicId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Valuation run of tenant {TenantId} failed.", tenant.PublicId);
                failed.Add(tenant.PublicId);
            }
        }

        if (failed.Count > 0)
        {
            throw new ValuationRunFailedException(failed);
        }
    }
}

/// <summary>R14: daily at 13:00 UTC on queue <c>valuation</c>, after <c>instrument-price-sync</c> at 12:00.</summary>
public static class ValuationRunRecurringJob
{
    public const string Cron = "0 13 * * *";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<ValuationRunJob>(
            ValuationRunJob.JobId,
            ValuationRunJob.Queue,
            job => job.Run(CancellationToken.None),
            Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
