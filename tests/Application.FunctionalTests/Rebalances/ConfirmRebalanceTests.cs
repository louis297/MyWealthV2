using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R13–R15 (docs/features/rebalances.md §10, confirm): a person confirms a proposal and the
/// first wave of ordinary orders is placed, the sells first.</summary>
public class ConfirmRebalanceTests : TestBase
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

        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/confirm", null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/confirm", world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/confirm", system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RebalanceHttp.ConfirmAsync(world.TenantAdmin, theirs)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RebalanceHttp.ConfirmAsync(await world.Orders.OtherAdviserAsync(), id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RebalanceHttp.RebalanceAsync(theirs)).Status.ShouldBe(RebalanceStatus.Proposed);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task Confirm_WithSells_PlacesOneWaveOfSellOrders_ByTheConfirmer_AndNoBuyYet()
    {
        var world = await RebalanceWorld.CreateAsync();
        var old = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD", "USD", 25m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, old, 40m, price: 25m);
        var (_, id) = await world.ProposeAsync();

        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("Selling");
        item.GetProperty("waves").GetInt32().ShouldBe(1);
        item.GetProperty("confirmedBy").GetProperty("id").GetGuid().ShouldBe(world.Orders.People.Adviser.PublicId);
        item.GetProperty("confirmedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        var wave = item.GetProperty("orders").EnumerateArray().ShouldHaveSingleItem();
        (wave.GetProperty("wave").GetInt32(), wave.GetProperty("side").GetString(),
                wave.GetProperty("instrument").GetProperty("symbol").GetString(), wave.GetProperty("quantity").GetDecimal(),
                wave.GetProperty("filledQuantity").GetDecimal(), wave.GetProperty("status").GetString())
            .ShouldBe((1, "Sell", "OLD", 40m, 0m, "Placed"));

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        rebalance.Connection.ShouldBe("simulator");
        var order = (await RebalanceHttp.OrdersOfAsync(id)).ShouldHaveSingleItem();
        order.RebalanceId.ShouldBe(rebalance.Id);
        order.Memo.ShouldBe("Rebalance");
        order.Connection.ShouldBe("simulator");
        order.PlacedByUserId.ShouldBe(rebalance.ConfirmedByUserId!.Value);
        order.ReservedQuantity.ShouldBe(40m);
        // ADR 0021 rule 4: nothing is sent to the broker in the request.
        BrokerHttp.Orders.Placed.ShouldBeEmpty();
        (await BrokerHttp.SendAsync()).ShouldNotBeNull();
        BrokerHttp.Orders.Placed.Count.ShouldBe(1);
    }

    [Test]
    public async Task Confirm_WithNoSell_StartsWithABuyWave_SizedSoItsReservationsFitTheAvailableCash()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();

        await RebalanceHttp.ConfirmedAsync(world.TenantAdmin, id);

        (await RebalanceHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Buying");
        var orders = await RebalanceHttp.OrdersOfAsync(id);
        orders.Select(order => order.Side).ShouldBe([OrderSide.Buy, OrderSide.Buy]);
        orders[0].Quantity.ShouldBeInRange(56.5m, 57.2m);
        orders[1].Quantity.ShouldBeInRange(75.4m, 76.2m);
        orders.ShouldAllBe(order => order.PriceLimit == OrderRules.PriceLimit(OrderSide.Buy, order.ReferencePrice));
        var (cash, reserved, available) = await world.Orders.CashAsync();
        cash.ShouldBe(10000m);
        reserved.ShouldBe(orders.Sum(order => order.ReservedCash));
        reserved.ShouldBeLessThanOrEqualTo(10000m);
        available.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Test]
    public async Task Confirm_OfAPlanThatChangedToNothing_EndsNoChange_AndPlacesNoOrder()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.AcmeId, 59.4m, price: 100m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.BetaId, 79.2m, price: 50m);

        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("NoChange");
        item.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Confirm_OfAPlanThatFailsNow_Returns400WithItsCode_AndStaysProposed()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.DisableInstrumentAsync(world.BetaId);

        var response = await RebalanceHttp.ConfirmAsync(world.Adviser, id);

        (await RebalanceHttp.CodeAsync(response)).ShouldBe("instrument_inactive");
        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        rebalance.Status.ShouldBe(RebalanceStatus.Proposed);
        rebalance.ConfirmedAt.ShouldBeNull();
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Confirm_WhenTheFirmLostItsBrokerConnection_Returns400NoBrokerConnection()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await OrderHttp.SetBrokerConnectionAsync(world.Tenant.Id, null);

        (await RebalanceHttp.CodeAsync(await RebalanceHttp.ConfirmAsync(world.Adviser, id))).ShouldBe("no_broker_connection");
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task Confirm_OfAnExpiredProposal_Returns400AtStatus()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ExpireAsync(id);

        (await RebalanceHttp.StatusErrorsAsync(await RebalanceHttp.ConfirmAsync(world.Adviser, id)))
            .ShouldBe(["This proposal has expired. Propose the rebalance again."]);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task ASecondConfirm_Returns400AtStatus_AndPlacesNothingMore()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        (await RebalanceHttp.StatusErrorsAsync(await RebalanceHttp.ConfirmAsync(world.Adviser, id)))
            .ShouldBe(["This rebalance is Buying and cannot be confirmed."]);
        (await RebalanceHttp.OrdersOfAsync(id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task Confirm_WithAStaleRowVersion_Returns409_AndWithoutOne_Returns400()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();

        var stale = await RebalanceHttp.ConfirmAsync(world.Adviser, id, OrderHttp.StaleRowVersion);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).ShouldContain("stale_row_version");
        (await RebalanceHttp.Send(HttpMethod.Post, $"/rebalances/{id}/confirm", world.Adviser, new { })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await RebalanceHttp.RebalanceAsync(id)).Status.ShouldBe(RebalanceStatus.Proposed);
    }

    [Test]
    public async Task Confirm_OfACancelledProposal_Returns400AtStatus()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, first) = await world.ProposeAsync();
        await world.ProposeAsync();

        (await RebalanceHttp.StatusErrorsAsync(await RebalanceHttp.ConfirmAsync(world.Adviser, first)))
            .ShouldBe(["This rebalance is Cancelled and cannot be confirmed."]);
    }
}
