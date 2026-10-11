using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class GetAccountHoldingsTests : TestBase
{
    private const string BookedAt = "2026-09-27T09:00:00+12:00";

    [Test]
    public async Task Get_ReturnsHoldingItemsWithoutAPrice()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenBrokerageAsync(tokens.AccessToken, customer.PublicId, "Held book");
        var bnd = await CreateInstrumentAsync(tokens.AccessToken, "BND", "USD");
        var vti = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(
            tokens.AccessToken,
            accountId,
            new[]
            {
                new { instrumentId = vti, quantity = 100m, cost = 1500.00m },
                new { instrumentId = bnd, quantity = 2m, cost = 0m }
            })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["items"]);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);

        var bndItem = items[0];
        bndItem.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "accountId", "instrumentId", "symbol", "name", "quantity", "cost", "costCurrency"
        ]);
        bndItem.GetProperty("id").GetGuid().ShouldBe(await HoldingPublicIdAsync(accountId, bnd));
        bndItem.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        bndItem.GetProperty("instrumentId").GetGuid().ShouldBe(bnd);
        bndItem.GetProperty("symbol").GetString().ShouldBe("BND");
        bndItem.GetProperty("name").GetString().ShouldBe("BND fund");
        bndItem.GetProperty("quantity").GetDecimal().ShouldBe(2m);
        bndItem.GetProperty("cost").GetDecimal().ShouldBe(0m);
        bndItem.GetProperty("costCurrency").GetString().ShouldBe("USD");

        var vtiItem = items[1];
        vtiItem.GetProperty("id").GetGuid().ShouldBe(await HoldingPublicIdAsync(accountId, vti));
        vtiItem.GetProperty("symbol").GetString().ShouldBe("VTI");
        vtiItem.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        vtiItem.GetProperty("cost").GetDecimal().ShouldBe(1500.00m);
        vtiItem.GetProperty("costCurrency").GetString().ShouldBe("USD");

        var account = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", tokens.AccessToken);
        using var accountJson = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        accountJson.RootElement.TryGetProperty("holdings", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Get_EmptyBook_ReturnsAnEmptyItemList()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenBrokerageAsync(tokens.AccessToken, customer.PublicId, "Empty book");

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Get_UnknownOtherTenantAndUnassigned_Return404()
    {
        var (tenant, _, _, adviserTokens) = await AccountHttp.SignInAdviser();
        (await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{Guid.NewGuid()}/holdings", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await OpenBrokerageAsync(southTokens.AccessToken, southCustomer.PublicId, "South book");
        (await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{southAccount}/holdings", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north.example", "Password1!", tenant.Id);
        var adminTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@north.example", "Password1!", "north-advisory");
        var unassigned = await OpenBrokerageAsync(adminTokens.AccessToken, otherCustomer.PublicId, "Pat book");
        (await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{unassigned}/holdings", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_GetOtherTenant_ReturnsTheHolding()
    {
        var (_, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenBrokerageAsync(adviserTokens.AccessToken, customer.PublicId, "Cross book");
        var vti = await CreateInstrumentAsync(adviserTokens.AccessToken, "VTI", "USD");
        (await PostOpeningAsync(
            adviserTokens.AccessToken,
            accountId,
            new[] { new { instrumentId = vti, quantity = 4m, cost = 40m } }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var admin = await AccountHttp.SignInSystemAdmin();
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/holdings", admin.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("instrumentId").GetGuid().ShouldBe(vti);
        item.GetProperty("quantity").GetDecimal().ShouldBe(4m);
        item.GetProperty("cost").GetDecimal().ShouldBe(40m);
    }

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await AccountHttp.Send(
            HttpMethod.Get, $"/accounts/{Guid.NewGuid()}/holdings", accessToken: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
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

        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{Guid.NewGuid()}/holdings", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> PostOpeningAsync(string accessToken, Guid accountId, object security) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = BookedAt,
            security
        }, Guid.NewGuid());

    private static async Task<Guid> HoldingPublicIdAsync(Guid accountPublicId, Guid instrumentPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await (
            from holding in db.Holdings.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on holding.AccountId equals account.Id
            join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id
            where account.PublicId == accountPublicId && instrument.PublicId == instrumentPublicId
            select holding.PublicId).SingleAsync();
    }

    private static async Task<Guid> OpenBrokerageAsync(string accessToken, Guid customerId, string name)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type = "Brokerage",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

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
}
