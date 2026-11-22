using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.MarketData;

/// <summary>Simulated instruments R5: <c>GET /market-data-connections</c>.</summary>
[Parallelizable]
public class MarketDataConnectionsTests : TestBase
{
    private const string Path = "/market-data-connections";

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path, accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_GetsTheConnection_WithNoFeedStateBeforeTheFirstRead()
    {
        var system = await TenantHttp.SignInSystemAdmin();

        using var json = await ReadAsync(system.AccessToken);

        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = root.GetProperty("items").EnumerateArray().Single();
        item.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["name", "adapter", "symbolSuffix", "prices", "corporateActions"], ignoreOrder: true);
        item.GetProperty("name").GetString().ShouldBe("simulator");
        item.GetProperty("adapter").GetString().ShouldBe("Simulator");
        item.GetProperty("symbolSuffix").GetString().ShouldBe(".SIM");
        item.GetProperty("prices").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("corporateActions").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task SystemAdmin_SeesWhenThePricesWereLastRead_AndWhyTheLastReadFailed()
    {
        var system = await TenantHttp.SignInSystemAdmin();
        await MarketDataHttp.RunAsync();
        MarketDataHttp.Prices.Fail(MarketDataFailure.Unauthorized);
        await MarketDataHttp.RunAsync();

        using var json = await ReadAsync(system.AccessToken);

        var prices = json.RootElement.GetProperty("items")[0].GetProperty("prices");
        prices.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["lastSucceededAt", "lastFailedAt", "lastFailure"], ignoreOrder: true);
        var succeeded = prices.GetProperty("lastSucceededAt").GetDateTimeOffset();
        prices.GetProperty("lastFailedAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(succeeded);
        prices.GetProperty("lastFailure").GetString().ShouldBe("Unauthorized");
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403()
    {
        var (_, _, customer, tenantAdmin) = await AccountHttp.SignInTenantAdmin(
            "Market Firm", "market-firm", "tess@market.example",
            adviserEmail: "ann@market.example", customerEmail: "ned@market.example");
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@market.example", "Password1!", "market-firm");
        var customerToken = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { tenantAdmin.AccessToken, adviser.AccessToken, customerToken.AccessToken })
        {
            (await TenantHttp.Send(HttpMethod.Get, Path, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    private static async Task<JsonDocument> ReadAsync(string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, Path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        return JsonDocument.Parse(content);
    }
}
