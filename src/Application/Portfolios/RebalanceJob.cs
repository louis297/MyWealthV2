using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Portfolios;

/// <summary>
/// Hangfire entry point for one Account after a weight change or <c>POST /portfolios/{id}/rebalance</c>
/// (rebalances R12). Engages the tenant filter with the Account's tenant, as <c>ReportJob</c> does with its execution
/// row, then proposes a rebalance whose id is the handle of the 202 body. An Account whose rebalance is in progress
/// gets no rebalance: that one is marked for a follow-up. A failed plan is stored as <c>Failed</c> and not retried.
/// </summary>
public sealed class RebalanceJob(ProposeRebalance proposals, IApplicationDbContext db, ITenantIdAccessor tenantIds)
{
    public async Task Execute(Guid accountId, Guid handle)
    {
        var tenantId = await db.Accounts
            .Where(row => row.PublicId == accountId)
            .Select(row => (int?)row.TenantId)
            .SingleOrDefaultAsync(CancellationToken.None);
        if (tenantId is not null)
        {
            tenantIds.Engage(tenantId.Value);
        }

        var account = await db.Accounts.SingleOrDefaultAsync(row => row.PublicId == accountId, CancellationToken.None)
                      ?? throw new NotFoundException("Account", accountId);
        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(account.Id, token);
            if (await proposals.OpenAsync(account.Id, token) is { IsInProgress: true } inProgress)
            {
                inProgress.MarkFollowUp();
                await db.SaveChangesAsync(token);
                return;
            }

            await proposals.ProposeAsync(account, RebalanceTrigger.Portfolio, byUserId: null, publicId: handle, token);
        }, CancellationToken.None);
    }
}
