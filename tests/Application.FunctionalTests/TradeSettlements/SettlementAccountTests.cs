using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R3–R5: the settlement account an order is placed with, and the send step that carries
/// it to the broker.</summary>
[Parallelizable]
public class SettlementAccountTests : TestBase
{
    private static FakeBrokerConnections Connections =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeBrokerConnections>();

    [Test]
    public async Task AnOrderInTheAccountsCurrency_WithAnActiveClientMoneyAccount_AndTheBrokersAccount_HasASettlementAccount_AndSendsIt()
    {
        Connections.SetBankAccount("USD");
        var world = await OrderWorld.CreateAsync();
        var clientMoney = await ClientMoneyAsync(world.Tenant.Id, "USD");

        var buy = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();

        var order = await OrderHttp.OrderAsync(buy);
        (order.ClientMoneyAccountId, order.SettlementAccountNumber).ShouldBe(((int?)clientMoney.Id, clientMoney.AccountNumber));
        var (_, request) = BrokerHttp.Orders.Placed.ShouldHaveSingleItem();
        request.SettlementAccount.ShouldBe(clientMoney.AccountNumber);
    }

    [Test]
    public async Task WithNoBrokerAccountForTheCurrency_TheOrderHasNone_IsStillPlaced_AndSendsNone()
    {
        Connections.SetBankAccount("NZD");
        var world = await OrderWorld.CreateAsync();

        var buy = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();

        var order = await OrderHttp.OrderAsync(buy);
        (order.ClientMoneyAccountId, order.SettlementAccountNumber).ShouldBe(((int?)null, (string?)null));
        BrokerHttp.Orders.Placed.ShouldHaveSingleItem().Order.SettlementAccount.ShouldBeNull();
    }

    [Test]
    public async Task WithTheClientMoneyAccountDisabled_TheOrderHasNone_AndIsStillPlaced()
    {
        Connections.SetBankAccount("USD");
        var world = await OrderWorld.CreateAsync();
        await DisableClientMoneyAsync(world.Tenant.Id, "USD");

        var buy = await world.PlaceAsync("Buy", 10m);

        var order = await OrderHttp.OrderAsync(buy);
        (order.ClientMoneyAccountId, order.SettlementAccountNumber).ShouldBe(((int?)null, (string?)null));
    }

    [Test]
    public async Task ASellInAnInstrumentQuotedInAnotherCurrency_HasNone_AndIsStillPlaced()
    {
        Connections.SetBankAccount("USD");
        Connections.SetBankAccount("NZD");
        var world = await OrderWorld.CreateAsync();
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD", 10m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, kiwi, 20m, price: 10m);

        var sell = await world.PlaceAsync("Sell", 5m, instrumentId: kiwi);
        await BrokerHttp.SendAsync();

        var order = await OrderHttp.OrderAsync(sell);
        (order.ClientMoneyAccountId, order.SettlementAccountNumber).ShouldBe(((int?)null, (string?)null));
        BrokerHttp.Orders.Placed.ShouldHaveSingleItem().Order.SettlementAccount.ShouldBeNull();
    }

    [Test]
    public async Task TwoTenants_EachOrderSettlesThroughItsOwnTenantsClientMoneyAccount()
    {
        Connections.SetBankAccount("USD");
        var north = await OrderWorld.CreateAsync();
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        var northMoney = await ClientMoneyAsync(north.Tenant.Id, "USD");
        var southMoney = await ClientMoneyAsync(south.Tenant.Id, "USD");

        var northOrder = await OrderHttp.OrderAsync(await north.PlaceAsync("Buy", 1m));
        var southOrder = await OrderHttp.OrderAsync(await south.PlaceAsync("Buy", 1m));

        northOrder.ClientMoneyAccountId.ShouldBe(northMoney.Id);
        southOrder.ClientMoneyAccountId.ShouldBe(southMoney.Id);
        southOrder.SettlementAccountNumber.ShouldBe(southMoney.AccountNumber);
    }

    [Test]
    public async Task WithTheOwnClientMoneyAccountDisabled_AnotherTenantsActiveOneIsNeverUsed()
    {
        Connections.SetBankAccount("USD");
        var north = await OrderWorld.CreateAsync();
        await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        await DisableClientMoneyAsync(north.Tenant.Id, "USD");

        var order = await OrderHttp.OrderAsync(await north.PlaceAsync("Buy", 1m));

        order.ClientMoneyAccountId.ShouldBeNull();
    }

    internal static async Task<ClientMoneyAccount> ClientMoneyAsync(int tenantId, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId && row.Currency == currency && row.IsActive);
    }

    private static async Task DisableClientMoneyAsync(int tenantId, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ClientMoneyAccounts.IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.Currency == currency && item.IsActive);
        row.Disable();
        await db.SaveChangesAsync();
    }
}
