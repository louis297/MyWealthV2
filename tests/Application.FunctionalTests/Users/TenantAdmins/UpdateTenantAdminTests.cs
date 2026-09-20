using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class UpdateTenantAdminTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{Guid.NewGuid()}", accessToken: null, new
            {
                name = "Alexandra Chen",
                rowVersion = "x"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        var person = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{person.PublicId}", tokens.AccessToken, new
            {
                name = "Tess Ltd",
                rowVersion = "x"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Ann Ltd",
                rowVersion = "x"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Dee Ltd",
                rowVersion = "x"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_Renames_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Alexandra Chen");
        item.RootElement.GetProperty("email").GetString().ShouldBe("Alex@north.example");
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
    }

    [Test]
    public async Task PutWithIsActive_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                isActive = false,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithEmail_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                email = "other@north.example",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                rowVersion = stale
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PutOnDisabledTenant_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken);
        await DisableTenantAsync(tenant.Id);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Put, $"/users/tenant-admins/{id}", tokens.AccessToken, new
            {
                name = "Alexandra Chen",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> CreateAsync(Guid tenantId, string accessToken)
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", accessToken, new
            {
                tenantId,
                name = "Alex Chen",
                email = "Alex@north.example",
                password = "Passw0rd!"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ReadRowVersion(Guid id, string accessToken)
    {
        using var json = await ReadItem(id, accessToken);
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> ReadItem(Guid id, string accessToken)
    {
        var response = await TenantAdminHttp.Send(HttpMethod.Get, $"/users/tenant-admins/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
