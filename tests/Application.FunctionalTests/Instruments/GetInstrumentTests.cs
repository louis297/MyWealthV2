using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

public class GetInstrumentTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments/{Guid.NewGuid()}", accessToken: null);
        item.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        item.Headers.Location.ShouldBeNull();
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

        (await InstrumentHttp.Send(HttpMethod.Get, "/instruments", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdminOfA_GetB_Returns404()
    {
        var (_, tokensA) = await InstrumentHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await InstrumentHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, "AIA", "Auckland International Airport", "NZD");

        var response = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments/{idB}", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var response = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_GetOtherTenant_Returns200()
    {
        var (_, adviserTokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(adviserTokens.AccessToken, "VTI", "Vanguard", "USD");
        var adminTokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments/{id}", adminTokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task TenantAdmin_DefaultPaging_ReturnsCurrentTenantOnly()
    {
        var (north, tokensA) = await InstrumentHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await InstrumentHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example");
        var vtiId = await CreateAsync(tokensA.AccessToken, "VTI", "Vanguard Total", "USD");
        await CreateAsync(tokensA.AccessToken, "AIA", "Auckland Airport", "NZD");
        var otherId = await CreateAsync(tokensB.AccessToken, "BHP", "BHP Group", "AUD");

        var response = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(vtiId);
        ids.ShouldNotContain(otherId);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("symbol").GetString().ShouldBe("AIA");
        first.GetProperty("name").GetString().ShouldBe("Auckland Airport");
        first.GetProperty("tenantId").GetGuid().ShouldBe(north.PublicId);
        first.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        first.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        first.TryGetProperty("price", out _).ShouldBeFalse();
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsDisabled()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var enabledId = await CreateAsync(tokens.AccessToken, "VTI", "Vanguard", "USD");
        var disabledId = await CreateAsync(tokens.AccessToken, "NZD-CASH", "Cash proxy", "NZD");
        await DisableInstrumentAsync(disabledId);

        var all = await ReadIds("/instruments", tokens.AccessToken);
        all.ShouldContain(enabledId);
        all.ShouldContain(disabledId);

        var enabledOnly = await ReadIds("/instruments?enabledOnly=true", tokens.AccessToken);
        enabledOnly.ShouldBe([enabledId]);

        var explicitFalse = await ReadIds("/instruments?enabledOnly=false", tokens.AccessToken);
        explicitFalse.ShouldBe(all, ignoreOrder: true);
    }

    [Test]
    public async Task Search_HitsSymbolNameAndPublicId()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var vtiId = await CreateAsync(tokens.AccessToken, "VTI", "Vanguard Total", "USD");
        var aiaId = await CreateAsync(tokens.AccessToken, "AIA", "Auckland Airport", "NZD");

        (await ReadIds("/instruments?search=vti", tokens.AccessToken)).ShouldBe([vtiId]);
        (await ReadIds("/instruments?search=Auckland", tokens.AccessToken)).ShouldBe([aiaId]);
        (await ReadIds($"/instruments?search={vtiId}", tokens.AccessToken)).ShouldBe([vtiId]);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();

        (await InstrumentHttp.Send(HttpMethod.Get, "/instruments?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await InstrumentHttp.Send(HttpMethod.Get, "/instruments?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await InstrumentHttp.Send(HttpMethod.Get, "/instruments?enabledOnly=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_ListWithoutTenantId_Returns400()
    {
        var tokens = await InstrumentHttp.SignInSystemAdmin();
        var response = await InstrumentHttp.Send(HttpMethod.Get, "/instruments", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemAdmin_ListWithTenantId_ReturnsThatTenant()
    {
        var (north, adviserTokens) = await InstrumentHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example");
        var id = await CreateAsync(adviserTokens.AccessToken, "VTI", "Vanguard", "USD");
        var adminTokens = await InstrumentHttp.SignInSystemAdmin();

        var response = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments?tenantId={north.PublicId}", adminTokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([id]);
    }

    [Test]
    public async Task TenantAdmin_ListWithTenantId_Returns400()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var response = await InstrumentHttp.Send(
            HttpMethod.Get, $"/instruments?tenantId={tenant.PublicId}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetAndListOnDisabledTenant_StillReturn200()
    {
        var (tenant, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateAsync(tokens.AccessToken, "VTI", "Vanguard", "USD");
        await DisableTenantAsync(tenant.Id);

        (await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InstrumentHttp.Send(HttpMethod.Get, "/instruments", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateAsync(
        string accessToken,
        string symbol,
        string name,
        string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name,
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ReadIds(string path, string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task DisableInstrumentAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == publicId);
        instrument.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
