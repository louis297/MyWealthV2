using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class CreateTenantAdminTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", accessToken: null, ValidBody(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
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
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
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
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_CreatesActiveTenantAdminWithIdentityDualWrite()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, new
            {
                tenantId = tenant.PublicId,
                name = "Alex Chen",
                email = "Alex@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var person = await FindPersonAsync(id);
        person.ShouldNotBeNull();
        person.Name.ShouldBe("Alex Chen");
        person.Email.ShouldBe("Alex@north.example");
        person.TenantId.ShouldBe(tenant.Id);
        person.Role.ShouldBe(UserRole.TenantAdmin);
        person.Status.ShouldBe(UserStatus.Active);
        person.AdviserId.ShouldBeNull();

        var identity = await FindIdentityAsync(person.IdentityUserId);
        identity.ShouldNotBeNull();
        identity.UserName.ShouldBe(id.ToString());
        identity.Email.ShouldBe("Alex@north.example");
        identity.TenantId.ShouldBe(tenant.Id);
    }

    [Test]
    public async Task DuplicateEmailSameTenantDifferentCase_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        (await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, new
            {
                tenantId = tenant.PublicId,
                name = "Alex Other",
                email = "Alex@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
    }

    [Test]
    public async Task SameEmailInTwoTenants_ReturnsTwo201s()
    {
        var north = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var south = await TestApp.CreateTenantAsync("South Co", "south-co");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        var first = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(north.PublicId));
        var second = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(south.PublicId));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        firstJson.RootElement.GetProperty("id").GetGuid()
            .ShouldNotBe(secondJson.RootElement.GetProperty("id").GetGuid());
    }

    [Test]
    public async Task UnknownTenant_Returns404()
    {
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(Guid.NewGuid()));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisabledTenant_Returns400DisabledTarget()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await DisableTenantAsync(tenant.Id);
        var tokens = await TenantAdminHttp.SignInSystemAdmin();

        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", tokens.AccessToken, ValidBody(tenant.PublicId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Tenant is disabled");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
    }

    private static object ValidBody(Guid tenantId) => new
    {
        tenantId,
        name = "Alex Chen",
        email = "alex@north.example",
        password = "Passw0rd!"
    };

    private static async Task<User?> FindPersonAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.SingleOrDefaultAsync(person => person.PublicId == publicId);
    }

    private static async Task<ApplicationUser?> FindIdentityAsync(string identityUserId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.SingleOrDefaultAsync(user => user.Id == identityUserId);
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
