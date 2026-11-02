using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;

namespace MyWealthV2.Application.Portfolios;

public sealed class RebalanceJob(AccountRebalancer rebalancer)
{
    public Task Execute(Guid accountId, Guid handle) =>
        rebalancer.RebalanceAsync(accountId, handle, CancellationToken.None);
}
