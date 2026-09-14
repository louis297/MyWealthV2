using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class GetTenantAdminTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{Guid.NewGuid()}", accessToken: null);
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

        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_GetById_ReturnsItemWithStatus()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Alex Chen", "Alex@north.example");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        json.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Alex Chen");
        json.RootElement.GetProperty("email").GetString().ShouldBe("Alex@north.example");
        json.RootElement.GetProperty("status").GetString().ShouldBe("active");
        json.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        json.RootElement.TryGetProperty("created", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("role", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("createdBy", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("identityUserId", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetById_AdviserPublicId_Returns404()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@north", "Password1!", tenant.Id);
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        var response = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{adviser.PublicId}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_UnknownPublicId_Returns404()
    {
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var response = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_DefaultPaging_ReturnsEnvelope()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var alexId = await CreateAsync(north.PublicId, tokens.AccessToken, "Alex Chen", "alex@north.example");
        await CreateAsync(south.PublicId, tokens.AccessToken, "Blair Ng", "blair@south.example");

        var response = await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldBe("Alex Chen");
        first.GetProperty("id").GetGuid().ShouldBe(alexId);
        first.GetProperty("status").GetString().ShouldBe("active");
        first.GetProperty("tenantId").GetGuid().ShouldBe(north.PublicId);
        first.TryGetProperty("role", out _).ShouldBeFalse();
    }

    [Test]
    public async Task TenantIdFilter_ReturnsOnlyThatTenant()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var alexId = await CreateAsync(north.PublicId, tokens.AccessToken, "Alex Chen", "alex@north.example");
        var blairId = await CreateAsync(south.PublicId, tokens.AccessToken, "Blair Ng", "blair@south.example");

        var filtered = await ReadIds($"/users/tenant-admins?tenantId={north.PublicId}", tokens.AccessToken);
        filtered.ShouldBe([alexId]);

        var unknown = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins?tenantId={Guid.NewGuid()}", tokens.AccessToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var empty = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        empty.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(0);
        empty.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);

        var unfiltered = await ReadIds("/users/tenant-admins", tokens.AccessToken);
        unfiltered.ShouldBe([alexId, blairId]);
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsDisabled()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var activeId = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Alex Chen", "alex@north.example");
        var disabledId = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Blair Ng", "blair@north.example");
        await DisablePersonAsync(disabledId);

        var omitted = await ReadIds("/users/tenant-admins", tokens.AccessToken);
        omitted.ShouldContain(activeId);
        omitted.ShouldContain(disabledId);

        var enabledOnly = await ReadIds("/users/tenant-admins?enabledOnly=true", tokens.AccessToken);
        enabledOnly.ShouldBe([activeId]);
    }

    [Test]
    public async Task Search_HitsNameAndEmail()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var alexId = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Alex Chen", "alex@north.example");
        var blairId = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Blair Ng", "blair@south.example");

        (await ReadIds("/users/tenant-admins?search=Alex", tokens.AccessToken)).ShouldBe([alexId]);
        (await ReadIds("/users/tenant-admins?search=blair@", tokens.AccessToken)).ShouldBe([blairId]);
        (await ReadIds($"/users/tenant-admins?search={alexId}", tokens.AccessToken)).ShouldBe([alexId]);
    }

    [Test]
    public async Task GetOnDisabledTenant_StillReturns200()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Alex Chen", "alex@north.example");
        await DisableTenantAsync(tenant.Id);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Get, $"/users/tenant-admins/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins?enabledOnly=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantAdminHttp.Send(HttpMethod.Get, "/users/tenant-admins?tenantId=not-a-guid", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateAsync(Guid tenantId, string accessToken, string name, string email)
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", accessToken, new
            {
                tenantId,
                name,
                email,
                password = "Passw0rd!"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ReadIds(string path, string accessToken)
    {
        var response = await TenantAdminHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task DisablePersonAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.PublicId == publicId);
        person.Disable();
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
