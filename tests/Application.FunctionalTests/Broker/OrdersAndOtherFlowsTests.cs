using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.CorporateActions;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R49, R50: an order and a portfolio assignment never share an account; a corporate action that
/// takes effect ends the open orders in its instrument.</summary>
[Parallelizable]
public class OrdersAndOtherFlowsTests : TestBase
{
    private static async Task<Guid> CreatePortfolioAsync(OrderWorld world)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.TenantAdmin, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = world.InstrumentId, targetWeight = 100m } }
        });
        return await CustodialHttp.ReadIdAsync(response);
    }

    private static Task<HttpResponseMessage> AssignAsync(OrderWorld world, Guid portfolioId) =>
        PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin, new { portfolioId });

    private static async Task MakeEffectiveAsync(OrderWorld world, Guid instrumentId)
    {
        var id = await CorporateActionHttp.CreateAsync(
            world.TenantAdmin, CorporateActionHttp.CreateBody(instrumentId, exDate: CorporateActionHttp.Today));
        var rowVersion = await CorporateActionHttp.RowVersionAsync(world.TenantAdmin, id);
        var response = await CorporateActionHttp.Send(
            HttpMethod.Post, $"{CorporateActionHttp.Root}/{id}/apply", world.TenantAdmin, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AssigningAPortfolio_ToAnAccountWithALiveOrder_Returns400AccountNotEligible_AndAfterTheCancelItIsAssigned()
    {
        var world = await OrderWorld.CreateAsync();
        var portfolioId = await CreatePortfolioAsync(world);
        var id = await world.PlaceAsync("Buy", 1m);

        var refused = await AssignAsync(world, portfolioId);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        var problem = await ProblemResponse.ReadAsync(refused);
        problem.Code.ShouldBe("account_not_eligible");
        problem.Detail.ShouldBe("Cancel the account's open orders before assigning a portfolio.");

        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var assigned = await AssignAsync(world, portfolioId);
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AFillThatWaitsToBePosted_KeepsItsOrderLive_SoThePortfolioIsNotAssigned()
    {
        var world = await OrderWorld.CreateAsync();
        var portfolioId = await CreatePortfolioAsync(world);
        var id = await world.PlaceAsync("Buy", 1m);
        await BrokerHttp.SendAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);
        BrokerHttp.Feed.AddFill(id, 1m, 100m);
        await BrokerHttp.RunReportsAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Open);
        (await OrderHttp.OrderAsync(id)).Status.ShouldBe(OrderStatus.Filled);

        var refused = await AssignAsync(world, portfolioId);

        (await ProblemResponse.ReadAsync(refused)).Detail.ShouldBe("Cancel the account's open orders before assigning a portfolio.");
    }

    [Test]
    public async Task ARebalance_MaySpendCashFromASaleThatHasNotSettled()
    {
        var world = await OrderWorld.CreateAsync(funding: 1000.00m);
        var other = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA", price: 20m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, other, 40m, price: 20m);
        var id = await world.PlaceAsync("Sell", 40m, instrumentId: other);
        await BrokerHttp.SendAsync();
        BrokerHttp.Feed.AddFill(id, 40m, 20m);
        await BrokerHttp.RunReportsAsync();
        // 1 000.00 less the 800.00 the holding cost, plus 795.00 from the sale, which has not settled.
        (await world.CashAsync()).Cash.ShouldBe(995.00m);
        var portfolioId = await CreatePortfolioAsync(world);

        var assigned = await AssignAsync(world, portfolioId);

        assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await assigned.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Success");
        // More than the 200.00 that was withdrawable went into the model.
        (await world.CashAsync()).Cash.ShouldBeLessThan(200.00m);
    }

    [Test]
    public async Task ACorporateActionThatTakesEffect_RequestsTheCancelOfAnOpenOrderInItsInstrument_AndCancelsAnUnsentOne()
    {
        var world = await OrderWorld.CreateAsync();
        var other = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA", price: 20m);
        var sent = await world.PlaceAsync("Buy", 2m);
        await BrokerHttp.SendAsync();
        var alreadyRequested = await world.PlaceAsync("Buy", 1m);
        await BrokerHttp.SendAsync();
        (await OrderHttp.CancelAsync(world.Adviser, alreadyRequested, "Customer changed their mind")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unsent = await world.PlaceAsync("Buy", 3m);
        var elsewhere = await world.PlaceAsync("Buy", 4m, instrumentId: other);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await MakeEffectiveAsync(world, world.InstrumentId);

        var working = await OrderHttp.GetAsync(world.TenantAdmin, sent);
        working.GetProperty("status").GetString().ShouldBe("Working");
        var request = working.GetProperty("cancelRequest");
        request.GetProperty("by").ValueKind.ShouldBe(JsonValueKind.Null);
        request.GetProperty("note").GetString().ShouldBe("Corporate action");
        request.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);

        var cancelled = await OrderHttp.GetAsync(world.TenantAdmin, unsent);
        cancelled.GetProperty("status").GetString().ShouldBe("Cancelled");
        cancelled.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        cancelled.GetProperty("cancelRequest").GetProperty("by").ValueKind.ShouldBe(JsonValueKind.Null);
        cancelled.GetProperty("cancelRequest").GetProperty("note").GetString().ShouldBe("Corporate action");

        (await OrderHttp.GetAsync(world.TenantAdmin, alreadyRequested)).GetProperty("cancelRequest").GetProperty("note").GetString()
            .ShouldBe("Customer changed their mind");
        var untouched = await OrderHttp.GetAsync(world.TenantAdmin, elsewhere);
        untouched.GetProperty("status").GetString().ShouldBe("Placed");
        untouched.GetProperty("cancelRequest").ValueKind.ShouldBe(JsonValueKind.Null);

        // The worker takes the platform's request to the broker like any other.
        await BrokerHttp.CancelAsync();
        BrokerHttp.Orders.Cancels.Select(call => call.ClientOrderId).ShouldBe([sent.ToString(), alreadyRequested.ToString()], ignoreOrder: true);
    }

    [Test]
    public async Task ACorporateAction_LeavesTheOrdersOfAnotherTenantAlone()
    {
        var north = await OrderWorld.CreateAsync();
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        var southOrder = await south.PlaceAsync("Buy", 1m);

        await MakeEffectiveAsync(north, north.InstrumentId);

        var item = await OrderHttp.GetAsync(south.TenantAdmin, southOrder);
        item.GetProperty("status").GetString().ShouldBe("Placed");
        item.GetProperty("cancelRequest").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
