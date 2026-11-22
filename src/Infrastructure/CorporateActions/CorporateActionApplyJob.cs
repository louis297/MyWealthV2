using Hangfire;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Infrastructure.BankFeed;

namespace MyWealthV2.Infrastructure.CorporateActions;

/// <summary>A run in which at least one corporate action or entitlement failed (docs/features/corporate-actions.md R25).</summary>
public sealed class CorporateActionApplyFailedException(int failed)
    : Exception($"Corporate action apply run left {failed} item(s) for the next run.");

/// <summary>
/// One run of <c>corporate-action-apply</c> (corporate-actions R24): makes due hand-entered corporate actions
/// effective and posts every pending entitlement, for every Tenant. One run at a time; no automatic retries, the next
/// run is the retry (ADR 0021 rule 15).
/// </summary>
public sealed class CorporateActionApplyJob(
    ApplyCorporateActions apply,
    SqlRunLock runLock,
    ILogger<CorporateActionApplyJob> logger)
{
    public const string JobId = "corporate-action-apply";

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task Run(CancellationToken cancellationToken)
    {
        await using var held = await runLock.TryAcquireAsync(JobId, cancellationToken);
        if (held is null)
        {
            logger.LogInformation("Another run of {JobId} is still going; this run does nothing.", JobId);
            return;
        }

        var outcome = await apply.RunAsync(null, cancellationToken);
        if (outcome.HasFailures)
        {
            // R25: reported to Hangfire as failed after every entitlement was tried.
            throw new CorporateActionApplyFailedException(outcome.Failed);
        }
    }
}

/// <summary>Registers <c>corporate-action-apply</c> on queue <c>market</c>, every minute (R24).</summary>
public static class CorporateActionApplyRecurringJob
{
    public const string Cron = "* * * * *";

    public static void Register(IRecurringJobManager recurring) =>
        recurring.AddOrUpdate<CorporateActionApplyJob>(
            CorporateActionApplyJob.JobId,
            MarketData.MarketDataRecurringJob.Queue,
            job => job.Run(CancellationToken.None),
            Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
