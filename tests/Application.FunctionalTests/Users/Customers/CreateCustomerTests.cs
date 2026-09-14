using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class CreateCustomerTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", accessToken: null, ValidBody(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await CustomerHttp.SignInSystemAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(Guid.NewGuid()));
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

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(adviser.PublicId));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_CreatesActiveCustomerWithIdentityDualWrite()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "Jordan@north.example",
                password = "Passw0rd!",
                adviserId = adviser.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var person = await FindPersonAsync(id);
        person.ShouldNotBeNull();
        person.Name.ShouldBe("Jordan Lee");
        person.Email.ShouldBe("Jordan@north.example");
        person.Role.ShouldBe(UserRole.Customer);
        person.Status.ShouldBe(UserStatus.Active);
        person.AdviserId.ShouldBe(adviser.Id);

        var identity = await FindIdentityAsync(person.IdentityUserId);
        identity.ShouldNotBeNull();
        identity.UserName.ShouldBe(id.ToString());
        identity.Email.ShouldBe("Jordan@north.example");
        identity.TenantId.ShouldBe(person.TenantId);
    }

    [Test]
    public async Task Adviser_OmittingAdviserId_AssignsSelf()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInAdviser();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "jordan@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();

        var person = await FindPersonAsync(id);
        person.ShouldNotBeNull();
        person.AdviserId.ShouldBe(adviser.Id);
        person.Role.ShouldBe(UserRole.Customer);
        person.Status.ShouldBe(UserStatus.Active);
    }

    [Test]
    public async Task Adviser_PassingAnotherAdviserId_Returns400()
    {
        var (tenant, _, tokens) = await CustomerHttp.SignInAdviser();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "jordan@north.example",
                password = "Passw0rd!",
                adviserId = other.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
    }

    [Test]
    public async Task DuplicateEmailSameTenantDifferentCase_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        (await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(adviser.PublicId)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Other",
                email = "Jordan@north.example",
                password = "Passw0rd!",
                adviserId = adviser.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
    }

    [Test]
    public async Task DuplicateEmailCollidingWithAdviser_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin(
            adviserEmail: "shared@north.example");

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "Shared@north.example",
                password = "Passw0rd!",
                adviserId = adviser.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SameEmailInTwoTenants_ReturnsTwo201s()
    {
        var (_, northAdviser, northTokens) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, southAdviser, southTokens) = await CustomerHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example", adviserEmail: "sam@south.example");

        var first = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", northTokens.AccessToken, ValidBody(northAdviser.PublicId));
        var second = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", southTokens.AccessToken, ValidBody(southAdviser.PublicId));

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
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "jordan@north.example",
                password = "Passw0rd!",
                adviserId = adviser.PublicId,
                tenantId = tenant.PublicId
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledTenant_Returns400DisabledTarget()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        await DisableTenantAsync(tenant.Id);

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(adviser.PublicId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Tenant is disabled");
        json.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Cannot create a Customer while the tenant is disabled. Enable the tenant first.");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Test]
    public async Task DisabledAdviser_Returns400DisabledUser()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        await DisablePersonAsync(adviser.PublicId);

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(adviser.PublicId));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(adviser.PublicId);
        json.RootElement.GetProperty("title").GetString().ShouldBe("User is disabled");
        json.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Cannot create a Customer on a disabled adviser. Enable or pick another adviser.");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Test]
    public async Task UnknownAdviserId_Returns404()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, ValidBody(Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_MissingAdviserId_Returns400()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                email = "jordan@north.example",
                password = "Passw0rd!"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static object ValidBody(Guid adviserId) => new
    {
        name = "Jordan Lee",
        email = "jordan@north.example",
        password = "Passw0rd!",
        adviserId
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

    private static async Task DisablePersonAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.PublicId == publicId);
        person.Disable();
        await db.SaveChangesAsync();
    }
}
