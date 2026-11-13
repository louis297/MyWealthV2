using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;

namespace MyWealthV2.Application.Portfolios;

/// <summary>
/// Hangfire entry point for one Account rebalance. Engages the tenant filter with the Account's
/// tenant before it rebalances, as <c>ReportJob</c> does with its execution row.
/// </summary>
public sealed class RebalanceJob(AccountRebalancer rebalancer, IApplicationDbContext db, ITenantIdAccessor tenantIds)
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

        await rebalancer.RebalanceAsync(accountId, handle, CancellationToken.None);
    }
}
