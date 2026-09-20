using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

public class GetAdviserTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await AdviserHttp.Send(HttpMethod.Get, "/users/advisers", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{Guid.NewGuid()}", accessToken: null);
        item.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        item.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await AdviserHttp.SignInSystemAdmin();

        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");

        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken))
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

        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_GetById_ReturnsItemWithStatus()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Sam Reed", "Sam@north.example");

        var response = await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        json.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Sam Reed");
        json.RootElement.GetProperty("email").GetString().ShouldBe("Sam@north.example");
        json.RootElement.GetProperty("status").GetString().ShouldBe("active");
        json.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        json.RootElement.TryGetProperty("created", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("role", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("createdBy", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("identityUserId", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetById_TenantAdminOrCustomerPublicId_Returns404()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@north", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@north", "Password1!", tenant.Id, adviser.Id);
        var tenantAdmin = await FindPersonByEmailAsync("tess@north.example");

        (await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{tenantAdmin!.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{customer.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_UnknownPublicId_Returns404()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var response = await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_GetBAdviser_Returns404()
    {
        var (_, tokensA) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await AdviserHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, "Blair Ng", "blair@south.example");

        var response = await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{idB}", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_DefaultPaging_ReturnsCurrentTenantOnly()
    {
        var (north, tokensA) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await AdviserHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var samId = await CreateAsync(tokensA.AccessToken, "Sam Reed", "sam@north.example");
        await CreateAsync(tokensA.AccessToken, "Alex Chen", "alex@north.example");
        var blairId = await CreateAsync(tokensB.AccessToken, "Blair Ng", "blair@south.example");

        var response = await AdviserHttp.Send(HttpMethod.Get, "/users/advisers", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(samId);
        ids.ShouldNotContain(blairId);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldBe("Alex Chen");
        first.GetProperty("status").GetString().ShouldBe("active");
        first.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        first.GetProperty("tenantId").GetGuid().ShouldBe(north.PublicId);
        first.TryGetProperty("role", out _).ShouldBeFalse();
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsDisabled()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var activeId = await CreateAsync(tokens.AccessToken, "Sam Reed", "sam@north.example");
        var disabledId = await CreateAsync(tokens.AccessToken, "Blair Ng", "blair@north.example");
        await DisablePersonAsync(disabledId);

        var omitted = await ReadIds("/users/advisers", tokens.AccessToken);
        omitted.ShouldContain(activeId);
        omitted.ShouldContain(disabledId);

        var enabledOnly = await ReadIds("/users/advisers?enabledOnly=true", tokens.AccessToken);
        enabledOnly.ShouldBe([activeId]);
    }

    [Test]
    public async Task Search_HitsNameAndEmail()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var samId = await CreateAsync(tokens.AccessToken, "Sam Reed", "sam@north.example");
        var blairId = await CreateAsync(tokens.AccessToken, "Blair Ng", "blair@south.example");

        (await ReadIds("/users/advisers?search=Sam", tokens.AccessToken)).ShouldBe([samId]);
        (await ReadIds("/users/advisers?search=blair@", tokens.AccessToken)).ShouldBe([blairId]);
        (await ReadIds($"/users/advisers?search={samId}", tokens.AccessToken)).ShouldBe([samId]);
    }

    [Test]
    public async Task GetOnDisabledTenant_StillReturns200()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Sam Reed", "sam@north.example");
        await DisableTenantAsync(tenant.Id);

        var response = await AdviserHttp.Send(
            HttpMethod.Get, $"/users/advisers/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();

        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AdviserHttp.Send(HttpMethod.Get, "/users/advisers?enabledOnly=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateAsync(string accessToken, string name, string email)
    {
        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", accessToken, new
            {
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
        var response = await AdviserHttp.Send(HttpMethod.Get, path, accessToken);
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

    private static async Task<User?> FindPersonByEmailAsync(string email)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.SingleOrDefaultAsync(person => person.Email == email);
    }
}
