using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

public class GetNetWorthTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var firm = await AccountHttp.Send(HttpMethod.Get, "/net-worth", accessToken: null);
        firm.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        firm.Headers.Location.ShouldBeNull();

        var customer = await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}/net-worth", accessToken: null);
        customer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        customer.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        (await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{customer.PublicId}/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Firm_SumsOpenCashAndQuoteCurrencyMarketValue()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var bank = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Everyday", "Bank", "NZD");
        var brokerage = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Broker", "Brokerage", "NZD");
        var closed = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Old book", "Brokerage", "EUR");
        await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Empty tin", "Cash", "GBP");

        (await PostCashAsync(tokens.AccessToken, bank, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(tokens.AccessToken, brokerage, 25m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(tokens.AccessToken, closed, 40m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var priced = await CreateInstrumentAsync(tokens.AccessToken, "AAA", "USD");
        var uncounted = await CreateInstrumentAsync(tokens.AccessToken, "BBB", "USD");
        await SetPriceAsync(priced, 20m, "USD");
        await SetPriceAsync(uncounted, 10m, "USD");
        (await PostOpeningAsync(tokens.AccessToken, brokerage, priced, 4m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, closed, uncounted, 3m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await CloseAccountAsync(closed);

        var before = DateTimeOffset.UtcNow;
        var response = await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken);
        var after = DateTimeOffset.UtcNow;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(["asOf", "currencies", "breakdowns"]);
        var asOf = root.GetProperty("asOf").GetDateTimeOffset();
        asOf.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        asOf.ShouldBeLessThanOrEqualTo(after.AddSeconds(1));

        var currencies = root.GetProperty("currencies").EnumerateArray().ToList();
        currencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["GBP", "NZD", "USD"]);
        AssertCurrency(currencies[0], "GBP", cash: 0m, holdings: 0m);
        AssertCurrency(currencies[1], "NZD", cash: 125m, holdings: 0m);
        AssertCurrency(currencies[2], "USD", cash: 0m, holdings: 80m);
    }

    [Test]
    public async Task CustomerRoute_MatchesThatCustomersFirmTotals_AndUnknownIdIs404()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var bank = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Everyday", "Bank", "NZD");
        (await PostCashAsync(tokens.AccessToken, bank, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var firm = await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken);
        var one = await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{customer.PublicId}/net-worth", tokens.AccessToken);
        firm.StatusCode.ShouldBe(HttpStatusCode.OK);
        one.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var firmJson = JsonDocument.Parse(await firm.Content.ReadAsStringAsync());
        using var oneJson = JsonDocument.Parse(await one.Content.ReadAsStringAsync());
        oneJson.RootElement.GetProperty("currencies").GetRawText()
            .ShouldBe(firmJson.RootElement.GetProperty("currencies").GetRawText());
        oneJson.RootElement.GetProperty("breakdowns").EnumerateObject().Select(property => property.Name)
            .ShouldBe(["byCustomer", "byAccountType", "topHoldings"]);

        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}/net-worth", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Adviser_OmitsUnassignedAndOtherTenantCustomers()
    {
        var (tenant, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var own = await OpenAccountAsync(adviserTokens.AccessToken, customer.PublicId, "Everyday", "Bank", "NZD");
        (await PostCashAsync(adviserTokens.AccessToken, own, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north.example", "Password1!", tenant.Id);
        var adminTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@north.example", "Password1!", "north-advisory");
        var unassigned = await OpenAccountAsync(adminTokens.AccessToken, otherCustomer.PublicId, "Pat bank", "Bank", "NZD");
        (await PostCashAsync(adminTokens.AccessToken, unassigned, 999m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var south = await OpenAccountAsync(southTokens.AccessToken, southCustomer.PublicId, "South bank", "Bank", "NZD");
        (await PostCashAsync(southTokens.AccessToken, south, 40m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var firm = await AccountHttp.Send(HttpMethod.Get, "/net-worth", adviserTokens.AccessToken);
        firm.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await firm.Content.ReadAsStringAsync());
        var currencies = json.RootElement.GetProperty("currencies").EnumerateArray().ToList();
        currencies.Count.ShouldBe(1);
        AssertCurrency(currencies[0], "NZD", cash: 100m, holdings: 0m);

        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{otherCustomer.PublicId}/net-worth", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{southCustomer.PublicId}/net-worth", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_RequiresTenantId_AndMayReadAnotherTenantsCustomer()
    {
        var (north, _, northCustomer, northTokens) = await AccountHttp.SignInAdviser();
        var northBank = await OpenAccountAsync(northTokens.AccessToken, northCustomer.PublicId, "North bank", "Bank", "NZD");
        (await PostCashAsync(northTokens.AccessToken, northBank, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var southBank = await OpenAccountAsync(southTokens.AccessToken, southCustomer.PublicId, "South bank", "Bank", "NZD");
        (await PostCashAsync(southTokens.AccessToken, southBank, 40m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var system = await AccountHttp.SignInSystemAdmin();
        (await AccountHttp.Send(HttpMethod.Get, "/net-worth", system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountHttp.Send(HttpMethod.Get, $"/net-worth?tenantId={Guid.NewGuid()}", system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var northRead = await AccountHttp.Send(
            HttpMethod.Get, $"/net-worth?tenantId={north.PublicId}", system.AccessToken);
        northRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var northJson = JsonDocument.Parse(await northRead.Content.ReadAsStringAsync());
        var northCurrencies = northJson.RootElement.GetProperty("currencies").EnumerateArray().ToList();
        northCurrencies.Count.ShouldBe(1);
        AssertCurrency(northCurrencies[0], "NZD", cash: 100m, holdings: 0m);

        var cross = await AccountHttp.Send(
            HttpMethod.Get, $"/users/customers/{southCustomer.PublicId}/net-worth", system.AccessToken);
        cross.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var crossJson = JsonDocument.Parse(await cross.Content.ReadAsStringAsync());
        var crossCurrencies = crossJson.RootElement.GetProperty("currencies").EnumerateArray().ToList();
        crossCurrencies.Count.ShouldBe(1);
        AssertCurrency(crossCurrencies[0], "NZD", cash: 40m, holdings: 0m);

        var (_, _, _, tenantAdmin) = await AccountHttp.SignInTenantAdmin(
            "West Co", "west-co", "tess@west.example", adviserEmail: "ann@west.example", customerEmail: "ned@west.example");
        (await AccountHttp.Send(
            HttpMethod.Get, $"/net-worth?tenantId={north.PublicId}", tenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static void AssertCurrency(JsonElement row, string currency, decimal cash, decimal holdings)
    {
        row.EnumerateObject().Select(property => property.Name).ShouldBe([
            "currency", "assets", "liabilities", "net"
        ]);
        row.GetProperty("currency").GetString().ShouldBe(currency);
        var assets = row.GetProperty("assets");
        assets.EnumerateObject().Select(property => property.Name).ShouldBe(["cash", "holdings", "total"]);
        assets.GetProperty("cash").GetDecimal().ShouldBe(cash);
        assets.GetProperty("holdings").GetDecimal().ShouldBe(holdings);
        assets.GetProperty("total").GetDecimal().ShouldBe(cash + holdings);
        var liabilities = row.GetProperty("liabilities");
        liabilities.EnumerateObject().Select(property => property.Name).ShouldBe(["total"]);
        liabilities.GetProperty("total").GetDecimal().ShouldBe(0m);
        row.GetProperty("net").GetDecimal().ShouldBe(cash + holdings);
    }

    private static async Task<Guid> OpenAccountAsync(
        string accessToken, Guid customerId, string name, string type, string currency)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostCashAsync(string accessToken, Guid accountId, decimal amount) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "TransferIn",
            amount,
            bookedAt = "2026-10-12T09:00:00+13:00"
        }, Guid.NewGuid());

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken, Guid accountId, Guid instrumentId, decimal quantity) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = "2026-10-12T09:00:00+13:00",
            security = new[] { new { instrumentId, quantity, cost = 1m } }
        }, Guid.NewGuid());

    private static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        var prices = FunctionalTestSetup.WebApiServices.GetRequiredService<MarketDataMock>();
        prices.SetPrice(instrument.Id, Money.Create(amount, currency));
    }

    private static async Task CloseAccountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        account.Close();
        await db.SaveChangesAsync();
    }
}
