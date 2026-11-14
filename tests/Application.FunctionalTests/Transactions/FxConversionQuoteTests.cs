using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>FX conversions R20: POST /transactions/fx/quote is read-only and runs R1, R3–R11.</summary>
public class FxConversionQuoteTests : TestBase
{
    private const string QuotePath = "/transactions/fx/quote";

    [Test]
    public async Task NoBearer_Returns401()
    {
        var response = await QuoteAsync(null, QuoteSellBody(Guid.NewGuid(), Guid.NewGuid(), 10m));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var world = await FxWorld.CreateAsync();
        var nedTokens = await TestApp.IssueAccessTokenAsync(world.Legs.Ned);

        (await QuoteAsync(nedTokens.AccessToken, QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task FixedSell_ReturnsTheRateAndBothAmountsAndWritesNothing()
    {
        var world = await FxWorld.CreateAsync();
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id);

        var response = await QuoteAsync(
            world.Legs.TenantAdmin.AccessToken, QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 1000.00m));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("baseCurrency").GetString().ShouldBe("NZD");
        root.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        root.GetProperty("rate").GetDecimal().ShouldBe(FxConversionTests.NzdUsd);
        root.GetProperty("rateAsOfDate").GetString().ShouldBe("2026-11-09");
        root.GetProperty("sell").GetProperty("amount").GetDecimal().ShouldBe(1000.00m);
        root.GetProperty("sell").GetProperty("currency").GetString().ShouldBe("NZD");
        root.GetProperty("buy").GetProperty("amount").GetDecimal().ShouldBe(591.20m);
        root.GetProperty("buy").GetProperty("currency").GetString().ShouldBe("USD");
        (await LegAccountsFixture.CountTransactionsAsync(world.Legs.Tenant.Id)).ShouldBe(before);
        (await FxConversionTests.CountFxDetailsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task FixedBuy_ReturnsTheSameAmountsAsCreate()
    {
        var world = await FxWorld.CreateAsync();
        await world.FundNzdAsync(2000m);

        var quote = await QuoteAsync(
            world.Legs.Ann.AccessToken, QuoteBuyBody(world.NzdAccountId, world.UsdAccountId, 500.00m));
        quote.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await quote.Content.ReadAsStringAsync());
        var rate = json.RootElement.GetProperty("rate").GetDecimal();
        var sell = json.RootElement.GetProperty("sell").GetProperty("amount").GetDecimal();
        sell.ShouldBe(845.74m);

        var id = await LegAccountsFixture.ReadIdAsync(await FxConversionTests.PostAsync(
            world.Legs.Ann.AccessToken,
            FxConversionTests.BuyBody(world.NzdAccountId, world.UsdAccountId, 500.00m, expectedRate: rate)));
        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NzdAccountId, -sell),
            (world.UsdAccountId, 500.00m)
        ]);
    }

    [Test]
    public async Task DoesNotCheckTheCashBalance()
    {
        var world = await FxWorld.CreateAsync();

        (await QuoteAsync(
                world.Legs.TenantAdmin.AccessToken, QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 1000m)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task BookedAtOmitted_UsesNow()
    {
        var world = await FxWorld.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await FxConversionTests.SetRateAsync("NZD", "USD", 0.60000000m, today);
        var body = QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body.Remove("bookedAt");

        var response = await QuoteAsync(world.Legs.TenantAdmin.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("rate").GetDecimal().ShouldBe(0.60000000m);
        json.RootElement.GetProperty("rateAsOfDate").GetString().ShouldBe(today.ToString("yyyy-MM-dd"));
    }

    [TestCase("memo", "USD for US ETF")]
    [TestCase("reference", "REF-1")]
    [TestCase("expectedRate", "0.5912")]
    [TestCase("type", "FxConversion")]
    [TestCase("amount", "10")]
    public async Task ForbiddenField_Returns400(string field, string value)
    {
        var world = await FxWorld.CreateAsync();
        var body = QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body[field] = value;

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BothAmounts_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        var body = QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body["buyAmount"] = 5m;

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRate_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        var body = QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m);
        body["bookedAt"] = "2026-11-14T09:00:00Z";

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MissingRate_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        var aud = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned AUD", "AUD");

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, QuoteSellBody(world.NzdAccountId, aud, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SameCurrencyOrTwoCustomers_Returns400()
    {
        var world = await FxWorld.CreateAsync();
        var savings = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Ned.PublicId, "Ned savings");
        var noraUsd = await InternalTransferTests.OpenAccountAsync(
            world.Legs.TenantAdmin.AccessToken, world.Legs.Nora.PublicId, "Nora USD", "USD");

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, QuoteSellBody(world.NzdAccountId, savings, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, QuoteSellBody(world.NzdAccountId, noraUsd, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AdviserNotAssigned_Returns404()
    {
        var world = await FxWorld.CreateAsync();

        (await QuoteAsync(world.Legs.Bob.AccessToken, QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AccountInAnotherTenant_Returns404(bool otherTenantIsFrom)
    {
        var world = await FxWorld.CreateAsync();
        var south = await FxConversionIsolationTests.SouthAsync();
        var body = otherTenantIsFrom
            ? QuoteSellBody(south.AccountId, world.UsdAccountId, 10m)
            : QuoteSellBody(world.NzdAccountId, south.AccountId, 10m);

        (await QuoteAsync(world.Legs.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await AccountHttp.SignInSystemAdmin();
        body["tenantId"] = world.Legs.Tenant.PublicId;
        (await QuoteAsync(system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdminMustSendTenantId()
    {
        var world = await FxWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();
        var body = QuoteSellBody(world.NzdAccountId, world.UsdAccountId, 10m);

        (await QuoteAsync(system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body["tenantId"] = world.Legs.Tenant.PublicId;
        (await QuoteAsync(system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Dictionary<string, object?> QuoteSellBody(Guid fromAccountId, Guid toAccountId, decimal sellAmount) =>
        new()
        {
            ["fromAccountId"] = fromAccountId,
            ["toAccountId"] = toAccountId,
            ["sellAmount"] = sellAmount,
            ["bookedAt"] = FxConversionTests.BookedAt
        };

    private static Dictionary<string, object?> QuoteBuyBody(Guid fromAccountId, Guid toAccountId, decimal buyAmount) =>
        new()
        {
            ["fromAccountId"] = fromAccountId,
            ["toAccountId"] = toAccountId,
            ["buyAmount"] = buyAmount,
            ["bookedAt"] = FxConversionTests.BookedAt
        };

    private static Task<HttpResponseMessage> QuoteAsync(string? accessToken, object body) =>
        TransactionHttp.Send(HttpMethod.Post, QuotePath, accessToken, body, idempotencyKey: null);
}
