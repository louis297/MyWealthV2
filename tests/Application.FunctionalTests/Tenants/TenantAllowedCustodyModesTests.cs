using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

// tenant-custody-modes R3–R5: allowedCustodyModes on POST /tenants, PUT /tenants/{id} and the tenant item.
public class TenantAllowedCustodyModesTests : TestBase
{
    private const string ShapeMessage = "AllowedCustodyModes must list External, Custodial or both, each once.";

    [Test]
    public async Task Post_ExternalOnly_Returns201AndTheItemCarriesIt()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken,
            CreateBody(["External"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await ReadIdAsync(response);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["External"]);
    }

    [Test]
    public async Task Post_BothInReverseOrder_ItemListsExternalFirst()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken,
            CreateBody(["Custodial", "External"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await ReadIdAsync(response);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["External", "Custodial"]);
    }

    [Test]
    public async Task Post_WithoutTheField_Returns400AndWritesNoTenant()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var before = await TestApp.CountAsync<Tenant>();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, new
        {
            name = "North Advisory",
            code = "north-advisory",
            reportingCurrency = "NZD"
        });

        await AssertShapeErrorAsync(response);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(before);
    }

    [Test]
    public async Task Post_WithNull_Returns400AndWritesNoTenant()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var before = await TestApp.CountAsync<Tenant>();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, CreateBody(null));

        await AssertShapeErrorAsync(response);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(before);
    }

    [TestCase("")]
    [TestCase("Other")]
    [TestCase("custodial")]
    [TestCase("External|External")]
    public async Task Post_BadShape_Returns400AndWritesNoTenant(string list)
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var before = await TestApp.CountAsync<Tenant>();

        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", tokens.AccessToken, CreateBody(Split(list)));

        await AssertShapeErrorAsync(response);
        (await TestApp.CountAsync<Tenant>()).ShouldBe(before);
    }

    [Test]
    public async Task Put_WithoutTheField_LeavesTheSetting()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var id = await CreateAsync(tokens.AccessToken, ["Custodial"]);
        var rowVersion = await ReadRowVersionAsync(id, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd",
            rowVersion
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["Custodial"]);
    }

    [Test]
    public async Task Put_WithNull_LeavesTheSetting()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var id = await CreateAsync(tokens.AccessToken, ["External"]);
        var rowVersion = await ReadRowVersionAsync(id, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken,
            UpdateBody(null, rowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["External"]);
    }

    [Test]
    public async Task Put_CustodialOnly_Returns204AndTheItemShowsIt()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var id = await CreateAsync(tokens.AccessToken, ["External", "Custodial"]);
        var rowVersion = await ReadRowVersionAsync(id, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken,
            UpdateBody(["Custodial"], rowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["Custodial"]);
    }

    [TestCase("")]
    [TestCase("Other")]
    [TestCase("custodial")]
    [TestCase("Custodial|Custodial")]
    public async Task Put_BadShape_Returns400AndLeavesTheSetting(string list)
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var id = await CreateAsync(tokens.AccessToken, ["External"]);
        var rowVersion = await ReadRowVersionAsync(id, tokens.AccessToken);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken,
            UpdateBody(Split(list), rowVersion));

        await AssertShapeErrorAsync(response);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["External"]);
    }

    [Test]
    public async Task Put_StaleRowVersion_Returns409AndLeavesTheSetting()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var id = await CreateAsync(tokens.AccessToken, ["External"]);
        var stale = await ReadRowVersionAsync(id, tokens.AccessToken);
        (await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken, new
        {
            name = "North Advisory Ltd",
            rowVersion = stale
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{id}", tokens.AccessToken,
            UpdateBody(["Custodial"], stale));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAllowedAsync($"/tenants/{id}", tokens.AccessToken)).ShouldBe(["External"]);
    }

    [Test]
    public async Task List_CarriesTheSettingOnEveryItem()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var narrowed = await CreateAsync(tokens.AccessToken, ["Custodial"]);
        var seeded = await TestApp.CreateTenantAsync("Seeded Firm", "seeded-firm");

        var response = await TenantHttp.Send(HttpMethod.Get, "/tenants?pageSize=100", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), Allowed);
        items[narrowed].ShouldBe(["Custodial"]);
        items[seeded.PublicId].ShouldBe(["External", "Custodial"]);
    }

    [TestCase(UserRole.TenantAdmin)]
    [TestCase(UserRole.Adviser)]
    public async Task ByCode_OwnFirm_CarriesTheSetting(UserRole role)
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var admin = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersionAsync(tenant.PublicId, admin.AccessToken);
        (await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", admin.AccessToken,
            UpdateBody(["Custodial"], rowVersion))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await TestApp.CreatePersonAsync(role, "Tess", "tess@north", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north", "Password1!", "north-advisory");

        (await ReadAllowedAsync("/tenants/by-code/north-advisory", tokens.AccessToken)).ShouldBe(["Custodial"]);
    }

    private static string[] Split(string list) =>
        list.Length == 0 ? [] : list.Split('|');

    private static object CreateBody(string[]? modes) => new
    {
        name = "North Advisory",
        code = "north-advisory",
        reportingCurrency = "NZD",
        allowedCustodyModes = modes
    };

    private static object UpdateBody(string[]? modes, string rowVersion) => new
    {
        name = "North Advisory",
        reportingCurrency = "NZD",
        allowedCustodyModes = modes,
        rowVersion
    };

    private static async Task<Guid> CreateAsync(string accessToken, string[] modes)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/tenants", accessToken, CreateBody(modes));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadIdAsync(response);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ReadRowVersionAsync(Guid id, string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{id}", accessToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<string?[]> ReadAllowedAsync(string path, string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Allowed(json.RootElement);
    }

    private static string?[] Allowed(JsonElement item) =>
        item.TryGetProperty("allowedCustodyModes", out var modes)
            ? modes.EnumerateArray().Select(mode => mode.GetString()).ToArray()
            : [];

    private static async Task AssertShapeErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
        var errors = json.RootElement.GetProperty("errors");
        var property = errors.EnumerateObject()
            .SingleOrDefault(entry => string.Equals(entry.Name, "AllowedCustodyModes", StringComparison.OrdinalIgnoreCase));
        property.Value.ValueKind.ShouldBe(JsonValueKind.Array, "errors.AllowedCustodyModes is missing");
        property.Value.EnumerateArray().Select(message => message.GetString()).ShouldContain(ShapeMessage);
    }
}
