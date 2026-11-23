using System.Net;
using MyWealthV2.Application.FunctionalTests.Portfolios;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Transaction source R9: a rebalance trade has <c>reference: null</c> and memo <c>Rebalance</c>.</summary>
[Parallelizable]
public class RebalanceReferenceTests : TestBase
{
    [Test]
    public async Task Assign_TradesHaveNoReference()
    {
        var world = await RebalanceTrades.AssignedAsync();

        var trades = await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId);

        // Rebalances R26: the trades are the fills of the rebalance's orders, with no reference.
        trades.ShouldNotBeEmpty();
        trades.ShouldAllBe(trade => trade.GetProperty("reference").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task Rebalance_ProposesAndWritesNoTrade()
    {
        var world = await RebalanceTrades.AssignedAsync();
        var before = await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId);
        (await RebalanceTrades.SplitHalfAndHalfAsync(world)).Dispose();
        // Rebalances R8: a proposal needs prices from the last 4 days.
        await Orders.OrderHttp.SetPriceAsync(world.First, 10m, Orders.OrderHttp.Today);
        await Orders.OrderHttp.SetPriceAsync(world.Second, 20m, Orders.OrderHttp.Today);

        // Rebalances R10, R26: the account route proposes; a rebalance trade is an order fill.
        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{world.AccountId}/rebalance", world.AccessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        content.ShouldContain("\"status\":\"Proposed\"");

        (await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId)).Count.ShouldBe(before.Count);
    }
}
