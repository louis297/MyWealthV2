using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.TradeSettlements;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R26 (docs/features/rebalances.md §10, reconciliation): a rebalance order gets a settlement
/// account like any order, so its fills settle through the trust account and leave the known difference
/// <c>Trades</c>.</summary>
public class RebalanceReconciliationTests : TestBase
{
    [Test]
    public async Task TheFillsOfARebalance_AreUnsettledTradesAndTradeFees_NotTrades()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeBrokerConnections>().SetBankAccount("USD");
        var world = await RebalanceWorld.CreateAsync();
        var clientMoney = await SettlementAccountTests.ClientMoneyAsync(world.Tenant.Id, "USD");

        await CustodialHttp.RebalanceAsync(world.Adviser, world.AccountId);

        (await RebalanceHttp.OrdersOfAsync((await RebalanceHttp.RebalancesOfAsync(world.AccountId)).Single().PublicId))
            .ShouldAllBe(order => order.ClientMoneyAccountId == clientMoney.Id);
        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);
        var kinds = Items(run).Select(item => item.Kind).ToList();
        kinds.ShouldContain("UnsettledTrades");
        kinds.ShouldContain("TradeFees");
        kinds.ShouldNotContain("Trades");
    }
}
