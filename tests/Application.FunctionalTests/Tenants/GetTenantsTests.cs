using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

public class GetTenantsTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await TenantHttp.Send(HttpMethod.Get, "/tenants", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{Guid.NewGuid()}", accessToken: null);
        item.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        item.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");

        (await TenantHttp.Send(HttpMethod.Get, "/tenants", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        (await TenantHttp.Send(HttpMethod.Get, "/tenants", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("dee@firmc", "Password1!", "firmc");

        (await TenantHttp.Send(HttpMethod.Get, "/tenants", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_DefaultPaging_ReturnsEnvelope()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(HttpMethod.Get, "/tenants", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldBe("North Advisory");
        first.GetProperty("code").GetString().ShouldBe("north-advisory");
        first.GetProperty("id").GetGuid().ShouldBe(north.PublicId);
        first.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        first.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        first.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        first.TryGetProperty("created", out _).ShouldBeTrue();
        first.TryGetProperty("createdBy", out _).ShouldBeFalse();
    }

    [Test]
    public async Task IsEnabledTrue_OmitsDisabled()
    {
        var enabled = await TestApp.CreateTenantAsync("Enabled Firm", "enabled-firm");
        var disabled = await TestApp.CreateTenantAsync("Disabled Firm", "disabled-firm");
        await DisableTenantAsync(disabled.Id);

        var tokens = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Get, "/tenants?isEnabled=true", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(enabled.PublicId);
        ids.ShouldNotContain(disabled.PublicId);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task Search_HitsNameAndCode()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var byName = await ReadIds("/tenants?search=North", tokens.AccessToken);
        byName.ShouldBe([north.PublicId]);

        var byCode = await ReadIds("/tenants?search=south-co", tokens.AccessToken);
        byCode.ShouldBe([south.PublicId]);

        var byPublicId = await ReadIds($"/tenants?search={north.PublicId}", tokens.AccessToken);
        byPublicId.ShouldBe([north.PublicId]);
    }

    [Test]
    public async Task GetById_ReturnsItem()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{tenant.PublicId}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("North Advisory");
        json.RootElement.GetProperty("code").GetString().ShouldBe("north-advisory");
        json.RootElement.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
        json.RootElement.GetProperty("isEnabled").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        json.RootElement.TryGetProperty("created", out _).ShouldBeTrue();
    }

    [Test]
    public async Task GetById_UnknownPublicId_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();

        (await TenantHttp.Send(HttpMethod.Get, "/tenants?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantHttp.Send(HttpMethod.Get, "/tenants?isEnabled=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task<List<Guid>> ReadIds(string path, string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }
}
