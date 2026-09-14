using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

public class CreateAdviserTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", accessToken: null, ValidBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await AdviserHttp.SignInSystemAdmin();

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, ValidBody());
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

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, ValidBody());
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_CreatesActiveAdviserWithIdentityDualWrite()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, new
            {
                name = "Sam Reed",
                email = "Sam@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var person = await FindPersonAsync(id);
        person.ShouldNotBeNull();
        person.Name.ShouldBe("Sam Reed");
        person.Email.ShouldBe("Sam@north.example");
        person.Role.ShouldBe(UserRole.Adviser);
        person.Status.ShouldBe(UserStatus.Active);
        person.AdviserId.ShouldBeNull();

        var identity = await FindIdentityAsync(person.IdentityUserId);
        identity.ShouldNotBeNull();
        identity.UserName.ShouldBe(id.ToString());
        identity.Email.ShouldBe("Sam@north.example");
        identity.TenantId.ShouldBe(person.TenantId);
    }

    [Test]
    public async Task DuplicateEmailSameTenantDifferentCase_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        (await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, ValidBody()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, new
            {
                name = "Sam Other",
                email = "Sam@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
    }

    [Test]
    public async Task DuplicateEmailCollidingWithTenantAdmin_Returns400()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin(
            email: "shared@north.example", password: "Password1!");

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, new
            {
                name = "Sam Reed",
                email = "Shared@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tenant.ShouldNotBeNull();
    }

    [Test]
    public async Task SameEmailInTwoTenants_ReturnsTwo201s()
    {
        var (_, northTokens) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, southTokens) = await AdviserHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");

        var first = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", northTokens.AccessToken, ValidBody());
        var second = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", southTokens.AccessToken, ValidBody());

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        firstJson.RootElement.GetProperty("id").GetGuid()
            .ShouldNotBe(secondJson.RootElement.GetProperty("id").GetGuid());
    }

    [Test]
    public async Task TenantIdInBody_Returns400()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, new
            {
                name = "Sam Reed",
                email = "sam@north.example",
                password = "Passw0rd!",
                tenantId = tenant.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledTenant_Returns400DisabledTarget()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        await DisableTenantAsync(tenant.Id);

        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", tokens.AccessToken, ValidBody());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Tenant is disabled");
        json.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Cannot create an Adviser while the tenant is disabled. Enable the tenant first.");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
    }

    private static object ValidBody() => new
    {
        name = "Sam Reed",
        email = "sam@north.example",
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
