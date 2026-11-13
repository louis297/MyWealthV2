using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;

namespace MyWealthV2.Application.Portfolios;

public sealed class RebalanceJob(AccountRebalancer rebalancer, IApplicationDbContext db, ITenantIdAccessor tenantIds)
{
    public Task Execute(Guid accountId, Guid handle)
    {
        _ = db;
        _ = tenantIds;
        return rebalancer.RebalanceAsync(accountId, handle, CancellationToken.None);
    }
}
