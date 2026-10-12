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

    [Test]
    public async Task Breakdowns_GroupCustomersAndAccountTypes_AndRankHoldings()
    {
        var (tenant, adviser, ned, tokens) = await AccountHttp.SignInAdviser();
        var amy = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Amy Able", "amy@north.example", "Password1!", tenant.Id, adviser.Id);

        var amyBank = await OpenAccountAsync(tokens.AccessToken, amy.PublicId, "Amy bank", "Bank", "NZD");
        var amyBroker = await OpenAccountAsync(tokens.AccessToken, amy.PublicId, "Amy broker", "Brokerage", "USD");
        var nedBank = await OpenAccountAsync(tokens.AccessToken, ned.PublicId, "Ned bank", "Bank", "NZD");
        await OpenAccountAsync(tokens.AccessToken, ned.PublicId, "Ned tin", "Cash", "GBP");
        var nedBroker = await OpenAccountAsync(tokens.AccessToken, ned.PublicId, "Ned broker", "Brokerage", "USD");
        (await PostCashAsync(tokens.AccessToken, amyBank, 10m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(tokens.AccessToken, nedBank, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var aaa = await CreateInstrumentAsync(tokens.AccessToken, "AAA", "USD");
        var mmm = await CreateInstrumentAsync(tokens.AccessToken, "MMM", "USD");
        var bond = await CreateInstrumentAsync(tokens.AccessToken, "BOND", "USD");
        var zed = await CreateInstrumentAsync(tokens.AccessToken, "ZED", "USD");
        var gone = await CreateInstrumentAsync(tokens.AccessToken, "GONE", "USD");
        await SetPriceAsync(aaa, 30m, "USD");
        await SetPriceAsync(mmm, 5m, "USD");
        await SetPriceAsync(gone, 100m, "USD");
        (await PostOpeningAsync(tokens.AccessToken, amyBroker, aaa, 1m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, nedBroker, aaa, 2m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, amyBroker, mmm, 4m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, amyBroker, bond, 2m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, amyBroker, zed, 3m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(tokens.AccessToken, amyBroker, gone, 1m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await ZeroHoldingAsync(amyBroker, gone);

        var response = await AccountHttp.Send(HttpMethod.Get, "/net-worth", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var currencies = root.GetProperty("currencies").EnumerateArray().ToList();
        currencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["GBP", "NZD", "USD"]);
        AssertCurrency(currencies[2], "USD", cash: 0m, holdings: 110m);

        var breakdowns = root.GetProperty("breakdowns");
        var byCustomer = breakdowns.GetProperty("byCustomer").EnumerateArray().ToList();
        byCustomer.Select(row => row.GetProperty("name").GetString()).ShouldBe(["Amy Able", "Ned North"]);
        byCustomer[0].EnumerateObject().Select(property => property.Name).ShouldBe(["customerId", "name", "currencies"]);
        byCustomer[0].GetProperty("customerId").GetGuid().ShouldBe(amy.PublicId);
        var amyCurrencies = byCustomer[0].GetProperty("currencies").EnumerateArray().ToList();
        amyCurrencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["NZD", "USD"]);
        AssertCurrency(amyCurrencies[0], "NZD", cash: 10m, holdings: 0m);
        AssertCurrency(amyCurrencies[1], "USD", cash: 0m, holdings: 50m);
        var nedCurrencies = byCustomer[1].GetProperty("currencies").EnumerateArray().ToList();
        nedCurrencies.Select(row => row.GetProperty("currency").GetString()).ShouldBe(["GBP", "NZD", "USD"]);
        AssertCurrency(nedCurrencies[2], "USD", cash: 0m, holdings: 60m);

        var byType = breakdowns.GetProperty("byAccountType").EnumerateArray().ToList();
        byType.Select(row => row.GetProperty("type").GetString()).ShouldBe(["Bank", "Brokerage", "Cash"]);
        byType[0].EnumerateObject().Select(property => property.Name).ShouldBe(["type", "currencies"]);
        AssertCurrency(byType[0].GetProperty("currencies").EnumerateArray().Single(), "NZD", cash: 110m, holdings: 0m);
        AssertCurrency(byType[1].GetProperty("currencies").EnumerateArray().Single(), "USD", cash: 0m, holdings: 110m);
        AssertCurrency(byType[2].GetProperty("currencies").EnumerateArray().Single(), "GBP", cash: 0m, holdings: 0m);

        var top = breakdowns.GetProperty("topHoldings").EnumerateArray().ToList();
        top.Select(row => row.GetProperty("symbol").GetString()).ShouldBe(["AAA", "MMM", "BOND", "ZED"]);
        top[0].EnumerateObject().Select(property => property.Name).ShouldBe([
            "instrumentId", "symbol", "quantity", "marketValue", "currency"
        ]);
        top[0].GetProperty("instrumentId").GetGuid().ShouldBe(aaa);
        top[0].GetProperty("quantity").GetDecimal().ShouldBe(3m);
        top[0].GetProperty("marketValue").GetDecimal().ShouldBe(90m);
        top[0].GetProperty("currency").GetString().ShouldBe("USD");
        top[2].GetProperty("marketValue").GetDecimal().ShouldBe(0m);
        top[2].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        top[2].GetProperty("currency").GetString().ShouldBe("USD");
        top[3].GetProperty("symbol").GetString().ShouldBe("ZED");
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

    private static async Task ZeroHoldingAsync(Guid accountPublicId, Guid instrumentPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var holdingId = await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on holding.AccountId equals account.Id
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where account.PublicId == accountPublicId && instrument.PublicId == instrumentPublicId
            select holding.Id).SingleAsync();
        await db.Holdings
            .Where(row => row.Id == holdingId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Quantity, 0m));
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
