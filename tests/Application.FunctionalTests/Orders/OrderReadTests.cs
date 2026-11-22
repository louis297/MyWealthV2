using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Custody;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>Orders R1, R15, R16: the order item and the list with its filters.</summary>
[Parallelizable]
public class OrderReadTests : TestBase
{
    [Test]
    public async Task ACustomer_ReadsTheirOwnOrders_WithoutTheMemoOrTheSendAttempts_AndNotAnotherCustomers()
    {
        var world = await OrderWorld.CreateAsync();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 2m);
        body["memo"] = "Asked by phone";
        var id = await CustodialHttp.ReadIdAsync(await OrderHttp.PostAsync(world.Adviser, body));

        var item = await OrderHttp.GetAsync(world.CustomerToken, id);
        var listed = (await OrderHttp.ListAsync(world.CustomerToken)).ShouldHaveSingleItem();

        foreach (var view in new[] { item, listed })
        {
            view.GetProperty("id").GetGuid().ShouldBe(id);
            view.GetProperty("memo").ValueKind.ShouldBe(JsonValueKind.Null);
            view.GetProperty("send").ValueKind.ShouldBe(JsonValueKind.Null);
            view.GetProperty("reservedCash").GetDecimal().ShouldBe(215m);
            view.GetProperty("priceLimit").GetDecimal().ShouldBe(105m);
        }

        (await OrderHttp.GetAsync(world.Adviser, id)).GetProperty("memo").GetString().ShouldBe("Asked by phone");
        (await OrderHttp.Send(HttpMethod.Get, $"/orders/{id}", world.OtherCustomerToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OrderHttp.ListIdsAsync(world.OtherCustomerToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task TheList_IsNewestPlacedFirst_WithoutFills_AndTheItemHasThem()
    {
        var world = await OrderWorld.CreateAsync();
        var first = await world.PlaceAsync("Buy", 1m);
        var second = await world.PlaceAsync("Buy", 2m);
        var third = await world.PlaceAsync("Buy", 3m);

        var items = await OrderHttp.ListAsync(world.TenantAdmin);

        items.Select(item => item.GetProperty("id").GetGuid()).ShouldBe([third, second, first]);
        items.All(item => !item.TryGetProperty("fills", out _)).ShouldBeTrue();
        (await OrderHttp.GetAsync(world.TenantAdmin, first)).GetProperty("fills").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Test]
    public async Task TheList_FiltersByAccountCustomerInstrumentSideAndStatus()
    {
        var world = await OrderWorld.CreateAsync();
        var other = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA", price: 20m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var olgas = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin, world.People.OtherCustomer.PublicId, "Olga's account");
        (await CustodialHttp.FundAsync(world.TenantAdmin, olgas, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var buy = await world.PlaceAsync("Buy", 1m);
        var sell = await world.PlaceAsync("Sell", 5m);
        var beta = await world.PlaceAsync("Buy", 1m, instrumentId: other);
        var olga = await OrderHttp.PlaceAsync(world.TenantAdmin, olgas, world.InstrumentId, "Buy", 1m);

        (await OrderHttp.ListIdsAsync(world.TenantAdmin)).ShouldBe([olga, beta, sell, buy]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, $"?accountId={world.AccountId}")).ShouldBe([beta, sell, buy]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, $"?customerId={world.People.OtherCustomer.PublicId}")).ShouldBe([olga]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, $"?instrumentId={other}")).ShouldBe([beta]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?side=sell")).ShouldBe([sell]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?status=Placed,Working,PartlyFilled")).ShouldBe([olga, beta, sell, buy]);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?status=Filled,Cancelled")).ShouldBeEmpty();
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?pendingFill=true")).ShouldBeEmpty();
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?page=2&pageSize=3")).ShouldBe([buy]);
        // An Adviser sees the orders of their own Customers only.
        (await OrderHttp.ListIdsAsync(world.Adviser)).ShouldBe([beta, sell, buy]);
    }

    [TestCase("?status=Open", "Status")]
    [TestCase("?status=Placed,Done", "Status")]
    [TestCase("?side=Short", "Side")]
    [TestCase("?tenantId=0f8fad5b-d9cb-469f-a165-70867728950e", "TenantId")]
    [TestCase("?pageSize=101", "PageSize")]
    public async Task TheList_WithAnUnknownFilterValue_OrATenantId_Returns400(string query, string key)
    {
        var world = await OrderWorld.CreateAsync();

        var response = await OrderHttp.Send(HttpMethod.Get, OrderHttp.Path + query, world.TenantAdmin);

        (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain(key);
    }

    [Test]
    public async Task AnIdThatIsNotAGuid_Or_NamesNoOrder_Returns404()
    {
        var world = await OrderWorld.CreateAsync();

        (await OrderHttp.Send(HttpMethod.Get, "/orders/not-a-guid", world.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OrderHttp.Send(HttpMethod.Get, $"/orders/{Guid.NewGuid()}", world.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
