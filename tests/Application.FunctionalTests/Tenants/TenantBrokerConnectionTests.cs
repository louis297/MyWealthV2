using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

/// <summary>Orders R3: a Tenant names its broker connection.</summary>
[Parallelizable]
public class TenantBrokerConnectionTests : TestBase
{
    private static object Body(string code, string? brokerConnection, bool withConnection = true) =>
        withConnection
            ? new
            {
                name = "Firm " + code,
                code,
                reportingCurrency = "NZD",
                allowedCustodyModes = new[] { "External", "Custodial" },
                brokerConnection
            }
            : new
            {
                name = "Firm " + code,
                code,
                reportingCurrency = "NZD",
                allowedCustodyModes = new[] { "External", "Custodial" }
            };

    private static async Task<JsonElement> GetAsync(string accessToken, string path)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, path, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static async Task<Guid> CreateAsync(string accessToken, string code, string? brokerConnection, bool withConnection = true)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", accessToken, Body(code, brokerConnection, withConnection));
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Test]
    public async Task Create_WithAConfiguredConnection_KeepsIt_OnGetListAndByCode()
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;

        var id = await CreateAsync(system, "with-broker", "simulator");

        (await GetAsync(system, $"/tenants/{id}")).GetProperty("brokerConnection").GetString().ShouldBe("simulator");
        (await GetAsync(system, "/tenants/by-code/with-broker")).GetProperty("brokerConnection").GetString().ShouldBe("simulator");
        var listed = (await GetAsync(system, "/tenants?search=with-broker")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        listed.GetProperty("brokerConnection").GetString().ShouldBe("simulator");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Create_WithoutAConnection_HasNone(bool sendNull)
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;

        var id = await CreateAsync(system, "no-broker", null, withConnection: sendNull);

        (await GetAsync(system, $"/tenants/{id}")).GetProperty("brokerConnection").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [TestCase("other")]
    [TestCase("Simulator")]
    [TestCase("")]
    public async Task Create_WithAnUnknownConnection_Returns400_UnderBrokerConnection(string brokerConnection)
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", system, Body("bad-broker", brokerConnection));

        var errors = await CustodialHttp.ErrorsAsync(response);
        errors["BrokerConnection"].ShouldBe(["Unknown broker connection."]);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants/by-code/bad-broker", system)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Update_WithAConfiguredConnection_SetsIt_AndWithoutOne_LeavesIt()
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;
        var id = await CreateAsync(system, "later-broker", null);

        var set = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", system, new
        {
            name = "Firm later-broker",
            brokerConnection = "simulator",
            rowVersion = (await GetAsync(system, $"/tenants/{id}")).GetProperty("rowVersion").GetString()
        });
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
        (await GetAsync(system, $"/tenants/{id}")).GetProperty("brokerConnection").GetString().ShouldBe("simulator");

        var omitted = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", system, new
        {
            name = "Firm later-broker Ltd",
            rowVersion = (await GetAsync(system, $"/tenants/{id}")).GetProperty("rowVersion").GetString()
        });
        omitted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var sentNull = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", system, new
        {
            name = "Firm later-broker Ltd",
            brokerConnection = (string?)null,
            rowVersion = (await GetAsync(system, $"/tenants/{id}")).GetProperty("rowVersion").GetString()
        });
        sentNull.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await GetAsync(system, $"/tenants/{id}");
        item.GetProperty("name").GetString().ShouldBe("Firm later-broker Ltd");
        item.GetProperty("brokerConnection").GetString().ShouldBe("simulator");
    }

    [Test]
    public async Task Update_WithAnUnknownConnection_Returns400_AndChangesNothing()
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;
        var id = await CreateAsync(system, "keep-broker", "simulator");

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", system, new
        {
            name = "Renamed",
            brokerConnection = "other",
            rowVersion = (await GetAsync(system, $"/tenants/{id}")).GetProperty("rowVersion").GetString()
        });

        var errors = await CustodialHttp.ErrorsAsync(response);
        errors["BrokerConnection"].ShouldBe(["Unknown broker connection."]);
        var item = await GetAsync(system, $"/tenants/{id}");
        item.GetProperty("name").GetString().ShouldBe("Firm keep-broker");
        item.GetProperty("brokerConnection").GetString().ShouldBe("simulator");
    }

    [Test]
    public async Task ATenantAdmin_ReadsTheConnection_OfTheirOwnFirm_ByCode()
    {
        var system = (await TenantHttp.SignInSystemAdmin()).AccessToken;
        await CreateAsync(system, "own-broker", "simulator");
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyWealthV2.Infrastructure.Data.ApplicationDbContext>();
            var tenantId = db.Tenants.Single(tenant => tenant.Code == "own-broker").Id;
            await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@own-broker.example", "Password1!", tenantId);
        }

        var tess = await FunctionalTestSetup.Oidc.SignInAsync("tess@own-broker.example", "Password1!", "own-broker");

        (await GetAsync(tess.AccessToken, "/tenants/by-code/own-broker")).GetProperty("brokerConnection").GetString().ShouldBe("simulator");
    }
}
