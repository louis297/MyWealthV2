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

        trades.ShouldNotBeEmpty();
        trades.ShouldAllBe(trade => trade.GetProperty("reference").ValueKind == System.Text.Json.JsonValueKind.Null);
        trades.ShouldAllBe(trade => trade.GetProperty("memo").GetString() == "Rebalance");
    }

    [Test]
    public async Task Rebalance_BuyAndSellTradesHaveNoReference()
    {
        var world = await RebalanceTrades.AssignedAsync();
        (await RebalanceTrades.SplitHalfAndHalfAsync(world)).Dispose();

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{world.AccountId}/rebalance", world.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var trades = await RebalanceTrades.TradesAsync(world.AccessToken, world.AccountId);
        trades.Select(trade => trade.GetProperty("type").GetString()).ShouldContain("Sell");
        trades.Count(trade => trade.GetProperty("type").GetString() == "Buy").ShouldBeGreaterThanOrEqualTo(2);
        trades.ShouldAllBe(trade => trade.GetProperty("reference").ValueKind == System.Text.Json.JsonValueKind.Null);
        trades.ShouldAllBe(trade => trade.GetProperty("memo").GetString() == "Rebalance");
    }
}
