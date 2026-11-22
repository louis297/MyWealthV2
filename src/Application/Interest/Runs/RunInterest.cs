using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.Accruals;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Interest.Runs;

/// <summary>
/// Interest-run R20, R30: stores the run, then accrues and posts every Open Custodial account of the Tenant that is not
/// closing, in <c>Id</c> order, each in its own database transaction under the Account lock. A failure on one account
/// is logged and rolls back that account only. Reads ignore the tenant filter and name the Tenant themselves.
/// </summary>
public class RunInterest(
    IApplicationDbContext db,
    IAccountInterest accountInterest,
    TimeProvider time,
    ILogger<RunInterest> logger) : IRunInterest
{
    public async Task<InterestRun> RunAsync(
        int tenantId,
        DateOnly asOfDate,
        InterestRunTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken)
    {
        var run = InterestRun.Start(tenantId, asOfDate, trigger, ranByUserId, null, time.GetUtcNow());
        run.CreatedBy = SystemActor.Name;
        db.InterestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        var runId = run.Id;
        var runPublicId = run.PublicId;
        var postingDate = run.PostingDate;

        var accountIds = await EligibleAccounts(tenantId).OrderBy(account => account.Id).Select(account => account.Id)
            .ToListAsync(cancellationToken);
        var processed = 0;
        var failed = 0;
        var postings = 0;
        foreach (var accountId in accountIds)
        {
            db.ClearTrackedChanges();
            processed++;
            try
            {
                var posted = false;
                await db.ExecuteInTransactionAsync(async token =>
                {
                    await db.LockAccountAsync(accountId, token);
                    var account = await EligibleAccounts(tenantId).SingleOrDefaultAsync(row => row.Id == accountId, token);
                    if (account is null)
                    {
                        return;
                    }

                    var result = await accountInterest.AccrueAndPostAsync(account, asOfDate, postingDate, runPublicId, token);
                    posted = result.Posting is not null;
                }, cancellationToken);
                if (posted)
                {
                    postings++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                var publicId = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.Id == accountId).Select(row => row.PublicId).SingleOrDefaultAsync(CancellationToken.None);
                logger.LogError(exception, "Interest run {RunId} failed on account {AccountId}; the next run takes it up.",
                    runPublicId, publicId);
            }
        }

        db.ClearTrackedChanges();
        var stored = await db.InterestRuns.IgnoreQueryFilters().SingleAsync(row => row.Id == runId, cancellationToken);
        stored.Complete(processed, failed, postings, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return stored;
    }

    /// <summary>R20: Open Custodial accounts of the Tenant that are not closing.</summary>
    private IQueryable<Account> EligibleAccounts(int tenantId) =>
        db.Accounts.IgnoreQueryFilters().Where(account =>
            account.TenantId == tenantId
            && account.CustodyMode == CustodyMode.Custodial
            && account.Status == AccountStatus.Open
            && !db.AccountClosures.IgnoreQueryFilters().Any(closure =>
                closure.AccountId == account.Id && closure.Status == AccountClosureStatus.InProgress));
}
