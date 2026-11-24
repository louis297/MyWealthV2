using System.Net;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R26, R43, R44: a transfer out takes only the transferable quantity and reserves it.</summary>
[Parallelizable]
public class TransferOutQuantityTests : TestBase
{
    [Test]
    public async Task AnOutAboveTheHolding_Returns400AtQuantity()
    {
        var world = await TransferWorld.CreateAsync();

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 100.5m));

        (await CustodialHttp.ErrorsAsync(response))["Quantity"].ShouldBe(["The account has 100 ACME available to transfer."]);
        (await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 100m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnOutWithNoHolding_Returns400WithZero()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);

        var response = await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 1m));

        (await CustodialHttp.ErrorsAsync(response))["Quantity"].ShouldBe(["The account has 0 ACME available to transfer."]);
    }

    [Test]
    public async Task AnOut_CannotTakeSharesReservedForALiveSellOrder()
    {
        var world = await TransferWorld.CreateAsync();
        await world.Orders.PlaceAsync("Sell", 30m);

        var refused = await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 71m));

        (await CustodialHttp.ErrorsAsync(refused))["Quantity"].ShouldBe(["The account has 70 ACME available to transfer."]);
        (await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 70m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnOut_CannotTakeSharesOfABuyFillThatHasNotSettled()
    {
        var world = await TransferWorld.CreateAsync();
        var order = await world.Orders.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = BrokerHttp.Feed.AddFill(order, 10m, 100m);
        await BrokerHttp.RunReportsAsync();

        var refused = await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 101m));

        (await CustodialHttp.ErrorsAsync(refused))["Quantity"]
            .ShouldBe(["The account has 100 ACME available to transfer. 10 is from purchases that have not settled."]);
        var holding = (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value;
        holding.GetProperty("quantity").GetDecimal().ShouldBe(110m);
        holding.GetProperty("unsettledQuantity").GetDecimal().ShouldBe(10m);
        holding.GetProperty("transferableQuantity").GetDecimal().ShouldBe(100m);

        BrokerHttp.Feed.AddSettled(order, fill.Id!);
        await BrokerHttp.RunReportsAsync();
        (await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 110m))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task TheRequest_Reserves_AndTheHoldingItemShowsIt()
    {
        var world = await TransferWorld.CreateAsync();

        await world.RequestAsync("Out", 40m);

        var holding = (await TransferHttp.HoldingAsync(world.CustomerToken, world.AccountId, world.InstrumentId))!.Value;
        holding.GetProperty("reservedQuantity").GetDecimal().ShouldBe(40m);
        holding.GetProperty("availableQuantity").GetDecimal().ShouldBe(60m);
        holding.GetProperty("unsettledQuantity").GetDecimal().ShouldBe(0m);
        holding.GetProperty("transferableQuantity").GetDecimal().ShouldBe(60m);
        (await CustodialHttp.ErrorsAsync(await TransferHttp.PostAsync(world.Adviser, world.Body("Out", 61m))))["Quantity"]
            .ShouldBe(["The account has 60 ACME available to transfer."]);
    }

    [Test]
    public async Task ATransferIn_ReservesNothing()
    {
        var world = await TransferWorld.CreateAsync();

        await world.RequestAsync("In", 40m);

        var holding = (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value;
        holding.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        holding.GetProperty("transferableQuantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task ASellOrder_AndAnAdjustment_CannotTakeSharesReservedForATransferOut()
    {
        var world = await TransferWorld.CreateAsync();
        await world.RequestAsync("Out", 40m);

        var sell = await world.Orders.TryPlaceAsync("Sell", 61m);
        var adjustment = await CustodialHttp.PostAsync(
            world.TenantAdmin,
            AdjustmentWorld.Body(world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, -61m, -1525m)),
            AdjustmentWorld.Path);

        (await CustodialHttp.ErrorsAsync(sell))["Quantity"].ShouldBe(["The account has 60 ACME available to sell."]);
        (await CustodialHttp.ErrorsAsync(adjustment))["Security"].ShouldBe(["40 ACME is reserved for sell orders and transfers out."]);
    }

    [Test]
    public async Task AHoldingOnAnExternalAccount_HasNothingUnsettled_AndAllOfItTransferable()
    {
        var world = await TransferWorld.CreateAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.TenantAdmin, external, 5000m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await OrderHttp.InsertHoldingAsync(external, world.InstrumentId, 12m);

        var holding = (await TransferHttp.HoldingAsync(world.TenantAdmin, external, world.InstrumentId))!.Value;

        holding.GetProperty("unsettledQuantity").GetDecimal().ShouldBe(0m);
        holding.GetProperty("transferableQuantity").GetDecimal().ShouldBe(12m);
    }
}
