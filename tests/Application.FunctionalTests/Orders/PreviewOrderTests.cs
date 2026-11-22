using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>Orders R17: the preview of an order.</summary>
[Parallelizable]
public class PreviewOrderTests : TestBase
{
    private const string Path = "/orders/preview";

    private static Task<HttpResponseMessage> PreviewAsync(string? accessToken, Dictionary<string, object?> body) =>
        OrderHttp.Send(HttpMethod.Post, Path, accessToken, body);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    [Test]
    public async Task WithoutBearer_Returns401_AndASystemAdminOrACustomer_Returns403()
    {
        var world = await OrderWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 10m);

        (await PreviewAsync(null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PreviewAsync(system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PreviewAsync(world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ABuy_ShowsAboutWhatItCosts_AndTheMostItCanCost_AndWritesNothing()
    {
        var world = await OrderWorld.CreateAsync();

        var preview = await ReadAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 10m)));

        preview.EnumerateObject().Select(property => property.Name).ShouldBe([
            "currency", "referencePrice", "referencePriceDate", "priceLimit", "estimatedGross", "estimatedFee", "maximumGross",
            "maximumFee", "maximumCost", "availableCash", "availableQuantity"
        ], ignoreOrder: true);
        preview.GetProperty("currency").GetString().ShouldBe("USD");
        preview.GetProperty("referencePrice").GetDecimal().ShouldBe(100m);
        preview.GetProperty("referencePriceDate").GetString().ShouldBe(OrderHttp.Today.ToString("yyyy-MM-dd"));
        preview.GetProperty("priceLimit").GetDecimal().ShouldBe(105m);
        preview.GetProperty("estimatedGross").GetDecimal().ShouldBe(1000m);
        preview.GetProperty("estimatedFee").GetDecimal().ShouldBe(5.00m);
        preview.GetProperty("maximumGross").GetDecimal().ShouldBe(1050m);
        preview.GetProperty("maximumFee").GetDecimal().ShouldBe(5.25m);
        preview.GetProperty("maximumCost").GetDecimal().ShouldBe(1055.25m);
        preview.GetProperty("availableCash").GetDecimal().ShouldBe(5000.00m);
        preview.GetProperty("availableQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(0);
        (await world.CashAsync()).ShouldBe((5000.00m, 0m, 5000.00m));
    }

    [Test]
    public async Task ASell_ShowsWhatItBringsBeforeFees_AndTheAvailableQuantity_AndNoMaximumCost()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 100m);
        await world.PlaceAsync("Sell", 40m);

        var preview = await ReadAsync(await PreviewAsync(world.TenantAdmin, OrderHttp.Body(world.AccountId, world.InstrumentId, "Sell", 10m)));

        preview.GetProperty("priceLimit").GetDecimal().ShouldBe(95m);
        preview.GetProperty("estimatedGross").GetDecimal().ShouldBe(1000m);
        preview.GetProperty("estimatedFee").GetDecimal().ShouldBe(5.00m);
        preview.GetProperty("maximumGross").GetDecimal().ShouldBe(950m);
        preview.GetProperty("maximumCost").ValueKind.ShouldBe(JsonValueKind.Null);
        preview.GetProperty("availableQuantity").GetDecimal().ShouldBe(60m);
        preview.GetProperty("availableCash").GetDecimal().ShouldBe(2500.00m);
        (await OrderHttp.OrderCountAsync(world.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task APreview_AnswersWithTheRefusalsOfPlace()
    {
        var world = await OrderWorld.CreateAsync(funding: 500m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin, world.Customer.PublicId);
        var unpriced = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "NOPX", price: null);

        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(external, world.InstrumentId, "Buy", 1m))))["AccountId"]
            .ShouldBe(["An order can be placed only on a Custodial account."]);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, unpriced, "Buy", 1m))))["InstrumentId"]
            .ShouldBe(["No price is stored for NOPX."]);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 10m))))["Quantity"]
            .ShouldBe(["This order can cost up to 1055.25 USD with fees. The account's available cash is 500.00 USD."]);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Sell", 1m))))["Quantity"]
            .ShouldBe(["The account has 0 ACME available to sell."]);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 0m))))["Quantity"]
            .ShouldBe(["Quantity must be greater than 0 with at most 8 decimal places."]);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, OrderHttp.Body(world.AccountId, world.InstrumentId, "Hold", 1m)))).Keys
            .ShouldContain("Side");
    }

    [Test]
    public async Task APreview_WithATenantId_Returns400()
    {
        var world = await OrderWorld.CreateAsync();
        var body = OrderHttp.Body(world.AccountId, world.InstrumentId, "Buy", 1m);
        body["tenantId"] = world.Tenant.PublicId;

        (await CustodialHttp.ErrorsAsync(await PreviewAsync(world.Adviser, body))).Keys.ShouldContain("TenantId");
    }

    [Test]
    public async Task AnAccountOutsideTheCallersScope_OrOfAnotherTenant_Is404()
    {
        var north = await OrderWorld.CreateAsync();
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        var bob = await north.OtherAdviserAsync();

        (await PreviewAsync(bob, OrderHttp.Body(north.AccountId, north.InstrumentId, "Buy", 1m))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PreviewAsync(north.TenantAdmin, OrderHttp.Body(south.AccountId, north.InstrumentId, "Buy", 1m))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.ErrorsAsync(await PreviewAsync(north.TenantAdmin, OrderHttp.Body(north.AccountId, south.InstrumentId, "Buy", 1m))))["InstrumentId"]
            .ShouldBe(["Instrument was not found."]);
    }
}
