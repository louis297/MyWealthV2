using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Orders;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R27 (docs/features/rebalances.md §10): the order item carries its rebalance, and the list
/// filters by it.</summary>
public class OrderRebalanceIdTests : TestBase
{
    [Test]
    public async Task ARebalanceOrder_CarriesItsRebalance_ForEveryReader_AndAPersonsOrderHasNone()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false);
        var own = await world.Orders.PlaceAsync("Buy", 1m);
        await RebalanceHttp.InsertAssignmentAsync(world.AccountId, world.PortfolioId);
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        var rebalanceOrder = (await RebalanceHttp.OrdersOfAsync(id))[0].PublicId;

        foreach (var token in new[] { world.Adviser, world.TenantAdmin, world.CustomerToken })
        {
            (await OrderHttp.GetAsync(token, rebalanceOrder)).GetProperty("rebalanceId").GetGuid().ShouldBe(id);
            (await OrderHttp.GetAsync(token, own)).GetProperty("rebalanceId").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        var listed = await OrderHttp.ListAsync(world.Adviser, $"?accountId={world.AccountId}");
        listed.Single(item => item.GetProperty("id").GetGuid() == rebalanceOrder).GetProperty("rebalanceId").GetGuid().ShouldBe(id);
    }

    [Test]
    public async Task TheList_FiltersByRebalance()
    {
        var world = await RebalanceWorld.CreateAsync(assign: false);
        var own = await world.Orders.PlaceAsync("Buy", 1m);
        await RebalanceHttp.InsertAssignmentAsync(world.AccountId, world.PortfolioId);
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        var orders = (await RebalanceHttp.OrdersOfAsync(id)).Select(order => order.PublicId).ToList();

        (await OrderHttp.ListIdsAsync(world.Adviser, $"?rebalanceId={id}")).ShouldBe(orders, ignoreOrder: true);
        (await OrderHttp.ListIdsAsync(world.Adviser, $"?rebalanceId={Guid.NewGuid()}")).ShouldBeEmpty();
        (await OrderHttp.ListIdsAsync(world.Adviser)).ShouldContain(own);
    }

    [Test]
    public async Task TheFilter_IsAGuid()
    {
        var world = await RebalanceWorld.CreateAsync();

        (await OrderHttp.Send(HttpMethod.Get, "/orders?rebalanceId=nope", world.Adviser)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
