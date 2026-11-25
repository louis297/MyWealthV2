using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R19, R20, R24 (docs/features/rebalances.md §10, cancel): a person cancels a rebalance; what is
/// not filled is cancelled and what was filled stays. A rebalance order is not cancelled by itself.</summary>
[Parallelizable]
public class CancelRebalanceTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_ACustomerOrASystemAdmin_Returns403_AndAnotherTenant_Returns404()
    {
        var south = await RebalanceWorld.CreateAsync("South Advisory", "south-advisory");
        var (_, theirs) = await south.ProposeAsync();
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        var body = new { rowVersion = await RebalanceHttp.RowVersionAsync(id) };

        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/cancel", null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/cancel", world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/cancel", system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RebalanceHttp.CancelAsync(world.TenantAdmin, theirs)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RebalanceHttp.RebalanceAsync(theirs)).Status.ShouldBe(RebalanceStatus.Proposed);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task CancelOfAProposal_IsCancelledRequested_WithThePersonAndTheNote()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();

        (await RebalanceHttp.CancelAsync(world.Adviser, id, "  Customer is withdrawing next week ")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("cancelReason").GetString().ShouldBe("Requested");
        item.GetProperty("cancelRequest").GetProperty("note").GetString().ShouldBe("Customer is withdrawing next week");
        item.GetProperty("cancelRequest").GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Orders.People.Adviser.PublicId);
        item.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task CancelInProgress_BeforeTheFirstSend_CancelsItsOrdersAtOnce_AndTheRebalanceInTheRequest()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        (await RebalanceHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        (rebalance.Status, rebalance.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Requested));
        var orders = await RebalanceHttp.OrdersOfAsync(id);
        orders.ShouldAllBe(order => order.Status == OrderStatus.Cancelled && order.CancelNote == "Rebalance cancelled");
        (await world.Orders.CashAsync()).Reserved.ShouldBe(0m);
        await BrokerHttp.SendAsync();
        BrokerHttp.Orders.Placed.ShouldBeEmpty();
    }

    [Test]
    public async Task CancelInProgress_AfterTheSend_AsksTheBroker_BooksAFillThatCrossed_AndPlacesNoFurtherWave()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        var orders = await RebalanceHttp.OrdersOfAsync(id);

        (await RebalanceHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var requested = await RebalanceHttp.GetAsync(world.Adviser, id);
        requested.GetProperty("status").GetString().ShouldBe("Buying");
        requested.GetProperty("cancelRequest").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await RebalanceHttp.OrdersOfAsync(id)).ShouldAllBe(order => order.HasCancelRequest && order.Status == OrderStatus.Working);

        // The first order filled before the broker took the cancel; the second one is cancelled.
        BrokerHttp.Feed.AddFill(orders[0].PublicId, orders[0].Quantity, orders[0].ReferencePrice, "CN-X1");
        await BrokerHttp.CancelAsync();
        BrokerHttp.Feed.AddEnd(orders[1].PublicId, ExecutionReportKind.Cancelled, "Cancelled by the client");
        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.AdvanceAsync();

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        (rebalance.Status, rebalance.CancelReason).ShouldBe((RebalanceStatus.Cancelled, (RebalanceCancelReason?)RebalanceCancelReason.Requested));
        var ended = await RebalanceHttp.OrdersOfAsync(id);
        ended.Select(order => order.Status).ShouldBe([OrderStatus.Filled, OrderStatus.Cancelled]);
        ended.Count.ShouldBe(2);
        (await RebalanceTrades.TradesAsync(world.Adviser, world.AccountId)).Count.ShouldBe(1);
    }

    [Test]
    public async Task ASecondCancel_AndACancelOfAFinalRebalance_Return400AtStatus()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        (await RebalanceHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RebalanceHttp.StatusErrorsAsync(await RebalanceHttp.CancelAsync(world.Adviser, id)))
            .ShouldBe(["A cancellation was already requested for this rebalance."]);

        var (_, other) = await RebalanceWorldOther(world);
        (await RebalanceHttp.CancelAsync(world.Adviser, other)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RebalanceHttp.StatusErrorsAsync(await RebalanceHttp.CancelAsync(world.Adviser, other)))
            .ShouldBe(["This rebalance is Cancelled and cannot be cancelled."]);
    }

    [Test]
    public async Task Cancel_WithAStaleRowVersion_Returns409_AndANoteOver200_Returns400()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();

        (await RebalanceHttp.CancelAsync(world.Adviser, id, rowVersion: OrderHttp.StaleRowVersion)).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
        (await RebalanceHttp.CancelAsync(world.Adviser, id, new string('x', 201))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task CancelOfARebalanceOrder_ThroughOrders_Returns400AtStatus()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        var order = (await RebalanceHttp.OrdersOfAsync(id))[0];

        var response = await OrderHttp.CancelAsync(world.Adviser, order.PublicId);

        (await RebalanceHttp.StatusErrorsAsync(response)).ShouldBe(["This order belongs to a rebalance. Cancel the rebalance."]);
        (await OrderHttp.OrderAsync(order.PublicId)).Status.ShouldBe(OrderStatus.Placed);
    }

    [Test]
    public async Task APersonsOrder_OnAnAssignedAccount_IsStillRefused()
    {
        var world = await RebalanceWorld.CreateAsync();

        var response = await world.Orders.TryPlaceAsync("Buy", 1m);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Manual security trades are not allowed.");
    }

    /// <summary>A second, unconfirmed proposal on the same world's account once its first rebalance ended.</summary>
    private static async Task<(string, Guid)> RebalanceWorldOther(RebalanceWorld world)
    {
        await BrokerHttp.CancelAsync();
        foreach (var order in await RebalanceHttp.OrdersOfAsync((await RebalanceHttp.ListIdsAsync(world.Adviser, "?open=true")).Single()))
        {
            BrokerHttp.Feed.AddEnd(order.PublicId, ExecutionReportKind.Cancelled);
        }

        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.AdvanceAsync();
        return await world.ProposeAsync();
    }
}
