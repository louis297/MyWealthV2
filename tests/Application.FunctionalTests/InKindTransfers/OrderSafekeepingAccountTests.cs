using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R46, R47: an order carries the safekeeping account of its instrument's quote currency
/// and sends its number to the broker.</summary>
[Parallelizable]
public class OrderSafekeepingAccountTests : TestBase
{
    [Test]
    public async Task AnOrder_PlacedWithAnActiveSafekeepingAccountInTheQuoteCurrency_StoresIt_AndSendsItsNumber()
    {
        var world = await TransferWorld.CreateAsync();

        var id = await world.Orders.PlaceAsync("Buy", 1m);
        await BrokerHttp.SendAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.SafekeepingAccountId, order.SafekeepingAccountNumber).ShouldBe(((int?)world.Safekeeping.Id, world.Safekeeping.AccountNumber));
        BrokerHttp.Orders.Placed.ShouldHaveSingleItem().Order.SafekeepingAccount.ShouldBe(world.Safekeeping.AccountNumber);
    }

    [Test]
    public async Task AnOrder_WithNoSafekeepingAccount_OrOneInAnotherCurrencyOnly_HasNone_AndSendsNone()
    {
        var world = await TransferWorld.CreateAsync(safekeeping: false);
        await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "NZD");

        var id = await world.Orders.PlaceAsync("Buy", 1m);
        await BrokerHttp.SendAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.SafekeepingAccountId, order.SafekeepingAccountNumber).ShouldBe(((int?)null, (string?)null));
        BrokerHttp.Orders.Placed.ShouldHaveSingleItem().Order.SafekeepingAccount.ShouldBeNull();
    }

    [Test]
    public async Task AnOrderPlacedBeforeTheSafekeepingAccountWasAdded_KeepsNone()
    {
        var world = await TransferWorld.CreateAsync(safekeeping: false);
        var id = await world.Orders.PlaceAsync("Buy", 1m);
        await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");

        await BrokerHttp.SendAsync();

        (await OrderHttp.OrderAsync(id)).SafekeepingAccountId.ShouldBeNull();
        BrokerHttp.Orders.Placed.ShouldHaveSingleItem().Order.SafekeepingAccount.ShouldBeNull();
    }
}
