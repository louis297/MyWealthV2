using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Deposit matching R3: <c>GET /bank-connections</c>.</summary>
[Parallelizable]
public class BankConnectionsTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/bank-connections", accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_GetsNameAndAdapterOnly_OnPageOne()
    {
        var system = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, "/bank-connections", system.AccessToken);

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = root.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("name").GetString().ShouldBe("simulator");
        item.GetProperty("adapter").GetString().ShouldBe("Simulator");
        item.EnumerateObject().Select(property => property.Name).ShouldBe(["name", "adapter"], ignoreOrder: true);
    }

    [Test]
    public async Task TenantAdminAdviserAndCustomer_Return403()
    {
        var (_, _, customer, tenantAdmin) = await AccountHttp.SignInTenantAdmin(
            "Connections Firm", "connections-firm", "tess@connections.example",
            adviserEmail: "ann@connections.example", customerEmail: "ned@connections.example");
        var adviser = await FunctionalTestSetup.Oidc.SignInAsync("ann@connections.example", "Password1!", "connections-firm");
        var customerToken = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { tenantAdmin.AccessToken, adviser.AccessToken, customerToken.AccessToken })
        {
            (await TenantHttp.Send(HttpMethod.Get, "/bank-connections", token))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }
}
