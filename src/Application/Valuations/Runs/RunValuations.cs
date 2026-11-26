using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>
/// Valuation-run R2, R13: stores the run, then values every Custodial account of the Tenant, any status, in
/// <c>Id</c> order through the through date. A failure on one account is logged and does not stop the others.
/// Reads ignore the tenant filter and name the Tenant themselves. It reads local tables only.
/// </summary>
public class RunValuations(
    IApplicationDbContext db,
    IStoreAccountValuations store,
    TimeProvider time,
    ILogger<RunValuations> logger) : IRunValuations
{
    public async Task<ValuationRun> RunAsync(
        int tenantId,
        DateOnly throughDate,
        ValuationRunTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken)
    {
        // R4: no run stores a day after the last final day.
        var lastFinalDay = ValuationRules.LastFinalDay(LedgerDay.Of(time.GetUtcNow()));
        if (throughDate > lastFinalDay)
        {
            throw new InvalidOperationException(
                $"A valuation run cannot go through {throughDate:yyyy-MM-dd}; the last final day is {lastFinalDay:yyyy-MM-dd}.");
        }

        var run = ValuationRun.Start(tenantId, throughDate, trigger, ranByUserId, time.GetUtcNow());
        run.CreatedBy = SystemActor.Name;
        db.ValuationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        var runId = run.Id;

        var accountIds = await db.Accounts.IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId && account.CustodyMode == CustodyMode.Custodial)
            .OrderBy(account => account.Id)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
        var processed = 0;
        var failed = 0;
        var days = 0;
        var incomplete = 0;
        foreach (var accountId in accountIds)
        {
            db.ClearTrackedChanges();
            var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.Id == accountId, cancellationToken);
            var result = await store.ValueThroughAsync(account, throughDate, run, cancellationToken);
            if (!result.HadDays)
            {
                continue;
            }

            processed++;
            days += result.Valued;
            incomplete += result.Incomplete;
            if (result.Failure is { } failure)
            {
                failed++;
                // R12: logged with the account and the day; the next run starts the account at that day again.
                logger.LogError(failure,
                    "Valuation run {RunId} failed on account {AccountId} for {Day}; the next run starts the account there.",
                    run.PublicId, account.PublicId, result.FailedDay);
            }
        }

        db.ClearTrackedChanges();
        var stored = await db.ValuationRuns.IgnoreQueryFilters().SingleAsync(row => row.Id == runId, cancellationToken);
        stored.Complete(processed, failed, days, incomplete, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return stored;
    }
}
