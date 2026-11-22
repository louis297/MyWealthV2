using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Interest.Runs;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Interest;

/// <summary>A daily interest job in which the run of at least one Tenant, or at least one account, failed (interest-run R31).</summary>
public sealed class InterestRunFailedException(IReadOnlyList<Guid> tenants)
    : Exception($"The interest run failed for tenants {string.Join(", ", tenants)}.");

/// <summary>
/// One execution of <c>interest-run</c> (interest-run R31): a run with trigger <c>Scheduled</c> for yesterday (UTC) for
/// every Tenant that has at least one interest rate schedule, in <c>Id</c> order, a disabled Tenant included, each in
/// its own scope. A failed Tenant is logged and does not stop the others; the job then fails once every Tenant was
/// tried. No automatic retries: the next day's run takes up what is left (R19, R22).
/// </summary>
public sealed class InterestRunJob(
    IApplicationDbContext db,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<InterestRunJob> logger)
{
    public const string JobId = "interest-run";

    public const string Queue = "interest";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(CancellationToken cancellationToken)
    {
        var asOfDate = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);
        var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(tenant => db.InterestRateSchedules.IgnoreQueryFilters().Any(schedule => schedule.TenantId == tenant.Id))
            .OrderBy(tenant => tenant.Id)
            .Select(tenant => new { tenant.Id, tenant.PublicId })
            .ToListAsync(cancellationToken);

        var failed = new List<Guid>();
        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var run = await scope.ServiceProvider.GetRequiredService<IRunInterest>()
                    .RunAsync(tenant.Id, asOfDate, InterestRunTrigger.Scheduled, ranByUserId: null, cancellationToken);
                if (run.AccountsFailed > 0)
                {
                    logger.LogError("Interest run {RunId} of tenant {TenantId} failed on {Count} accounts.",
                        run.PublicId, tenant.PublicId, run.AccountsFailed);
                    failed.Add(tenant.PublicId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Interest run of tenant {TenantId} failed.", tenant.PublicId);
                failed.Add(tenant.PublicId);
            }
        }

        if (failed.Count > 0)
        {
            throw new InterestRunFailedException(failed);
        }
    }
}

/// <summary>R31: daily at 00:30 UTC on queue <c>interest</c>, just after the UTC day ends.</summary>
public static class InterestRunRecurringJob
{
    public const string Cron = "30 0 * * *";

    public static void Register(IRecurringJobManager recurring)
    {
        recurring.AddOrUpdate<InterestRunJob>(
            InterestRunJob.JobId,
            InterestRunJob.Queue,
            job => job.Run(CancellationToken.None),
            Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
