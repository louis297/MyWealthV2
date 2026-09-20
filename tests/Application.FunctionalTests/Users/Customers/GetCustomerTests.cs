using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class GetCustomerTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var list = await CustomerHttp.Send(HttpMethod.Get, "/users/customers", accessToken: null);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.Headers.Location.ShouldBeNull();

        var item = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}", accessToken: null);
        item.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        item.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await CustomerHttp.SignInSystemAdmin();

        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken))
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

        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_GetById_ReturnsItemWithStatusAndAdviserId()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Jordan Lee", "Jordan@north.example");

        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        json.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        json.RootElement.GetProperty("adviserId").GetGuid().ShouldBe(adviser.PublicId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Jordan Lee");
        json.RootElement.GetProperty("email").GetString().ShouldBe("Jordan@north.example");
        json.RootElement.GetProperty("status").GetString().ShouldBe("active");
        json.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        json.RootElement.TryGetProperty("created", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("role", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("createdBy", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("identityUserId", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetById_AdviserOrTenantAdminPublicId_Returns404()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var tenantAdmin = await FindPersonByEmailAsync("tess@north.example");

        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{adviser.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{tenantAdmin!.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_UnknownPublicId_Returns404()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();
        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_GetBCustomer_Returns404()
    {
        var (_, _, tokensA) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, adviserB, tokensB) = await CustomerHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example", adviserEmail: "sam@south.example");
        var idB = await CreateAsync(
            tokensB.AccessToken, adviserB.PublicId, "Blair Ng", "blair@south.example");

        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{idB}", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdmin_DefaultPaging_ReturnsCurrentTenantOnly()
    {
        var (north, adviserA, tokensA) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, adviserB, tokensB) = await CustomerHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example", adviserEmail: "sam@south.example");
        var jordanId = await CreateAsync(
            tokensA.AccessToken, adviserA.PublicId, "Jordan Lee", "jordan@north.example");
        await CreateAsync(
            tokensA.AccessToken, adviserA.PublicId, "Alex Chen", "alex@north.example");
        var blairId = await CreateAsync(
            tokensB.AccessToken, adviserB.PublicId, "Blair Ng", "blair@south.example");

        var response = await CustomerHttp.Send(HttpMethod.Get, "/users/customers", tokensA.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        ids.ShouldContain(jordanId);
        ids.ShouldNotContain(blairId);

        var first = json.RootElement.GetProperty("items")[0];
        first.GetProperty("name").GetString().ShouldBe("Alex Chen");
        first.GetProperty("status").GetString().ShouldBe("active");
        first.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        first.GetProperty("adviserId").GetGuid().ShouldBe(adviserA.PublicId);
        first.GetProperty("tenantId").GetGuid().ShouldBe(north.PublicId);
        first.TryGetProperty("role", out _).ShouldBeFalse();
    }

    [Test]
    public async Task EnabledOnlyTrue_OmitsDisabled()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var activeId = await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Jordan Lee", "jordan@north.example");
        var disabledId = await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Blair Ng", "blair@north.example");
        await DisablePersonAsync(disabledId);

        var omitted = await ReadIds("/users/customers", tokens.AccessToken);
        omitted.ShouldContain(activeId);
        omitted.ShouldContain(disabledId);

        var enabledOnly = await ReadIds("/users/customers?enabledOnly=true", tokens.AccessToken);
        enabledOnly.ShouldBe([activeId]);
    }

    [Test]
    public async Task Search_HitsNameAndEmail()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var jordanId = await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Jordan Lee", "jordan@north.example");
        var blairId = await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Blair Ng", "blair@south.example");

        (await ReadIds("/users/customers?search=Jordan", tokens.AccessToken)).ShouldBe([jordanId]);
        (await ReadIds("/users/customers?search=blair@", tokens.AccessToken)).ShouldBe([blairId]);
        (await ReadIds($"/users/customers?search={jordanId}", tokens.AccessToken)).ShouldBe([jordanId]);
    }

    [Test]
    public async Task TenantAdmin_AdviserIdFilter_ReturnsAssignedOnly()
    {
        var (tenant, adviserA, tokens) = await CustomerHttp.SignInTenantAdmin();
        var adviserB = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var jordanId = await CreateAsync(
            tokens.AccessToken, adviserA.PublicId, "Jordan Lee", "jordan@north.example");
        await CreateAsync(
            tokens.AccessToken, adviserB.PublicId, "Alex Chen", "alex@north.example");

        var filtered = await ReadIds(
            $"/users/customers?adviserId={adviserA.PublicId}", tokens.AccessToken);
        filtered.ShouldBe([jordanId]);
    }

    [Test]
    public async Task TenantAdmin_UnknownAdviserIdFilter_ReturnsEmpty()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Jordan Lee", "jordan@north.example");

        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers?adviserId={Guid.NewGuid()}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Adviser_ListAndGet_OnlyAssigned()
    {
        var (tenant, adviserA, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var adviserB = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var assignedId = await CreateAsync(
            adminTokens.AccessToken, adviserA.PublicId, "Jordan Lee", "jordan@north.example");
        var otherId = await CreateAsync(
            adminTokens.AccessToken, adviserB.PublicId, "Alex Chen", "alex@north.example");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");

        var ids = await ReadIds("/users/customers", adviserTokens.AccessToken);
        ids.ShouldBe([assignedId]);
        ids.ShouldNotContain(otherId);

        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{assignedId}", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{otherId}", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Adviser_AdviserIdOfSomeoneElse_Returns400()
    {
        var (tenant, _, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        await CreateAsync(
            adminTokens.AccessToken, other.PublicId, "Jordan Lee", "jordan@north.example");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");

        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers?adviserId={other.PublicId}", adviserTokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_AdviserIdOfSelf_IgnoresFilter()
    {
        var (_, adviser, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(
            adminTokens.AccessToken, adviser.PublicId, "Jordan Lee", "jordan@north.example");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");

        var ids = await ReadIds(
            $"/users/customers?adviserId={adviser.PublicId}", adviserTokens.AccessToken);
        ids.ShouldBe([id]);
    }

    [Test]
    public async Task GetOnDisabledTenant_StillReturns200()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(
            tokens.AccessToken, adviser.PublicId, "Jordan Lee", "jordan@north.example");
        await DisableTenantAsync(tenant.Id);

        var response = await CustomerHttp.Send(
            HttpMethod.Get, $"/users/customers/{id}", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task IllegalQuery_Returns400()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();

        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers?page=0", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustomerHttp.Send(HttpMethod.Get, "/users/customers?enabledOnly=maybe", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateAsync(
        string accessToken, Guid adviserId, string name, string email)
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", accessToken, new
            {
                name,
                email,
                password = "Passw0rd!",
                adviserId
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ReadIds(string path, string accessToken)
    {
        var response = await CustomerHttp.Send(HttpMethod.Get, path, accessToken);
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
