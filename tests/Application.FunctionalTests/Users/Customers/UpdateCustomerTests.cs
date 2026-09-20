using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class UpdateCustomerTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{Guid.NewGuid()}", accessToken: null, new
            {
                name = "Jordan Lee",
                rowVersion = "x"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await CustomerHttp.SignInSystemAdmin();

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Jordan Lee",
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

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Dee Ltd",
                rowVersion = "x"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_Renames_Returns204()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Jordan Lee Ltd");
        item.RootElement.GetProperty("email").GetString().ShouldBe("Jordan@north.example");
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.RootElement.GetProperty("adviserId").GetGuid().ShouldBe(adviser.PublicId);
    }

    [Test]
    public async Task TenantAdmin_ReassignToActiveAdviser_Returns204()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                adviserId = other.PublicId,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("adviserId").GetGuid().ShouldBe(other.PublicId);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Jordan Lee");
    }

    [Test]
    public async Task TenantAdmin_ReassignToDisabledAdviser_Returns400DisabledUser()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        await DisablePersonAsync(other.PublicId);
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee",
                adviserId = other.PublicId,
                rowVersion
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        json.RootElement.GetProperty("targetId").GetGuid().ShouldBe(other.PublicId);
        json.RootElement.GetProperty("title").GetString().ShouldBe("User is disabled");
        json.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Cannot reassign a Customer to a disabled adviser. Enable or pick another adviser.");
    }

    [Test]
    public async Task PutWithIsActive_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                isActive = false,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithEmail_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                email = "other@north.example",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adviser_PutWithAdviserId_Returns400()
    {
        var (_, adviser, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(adminTokens.AccessToken, adviser.PublicId);
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", adviserTokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                adviserId = Guid.NewGuid(),
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
    }

    [Test]
    public async Task Adviser_RenamesAssigned_Returns204()
    {
        var (_, adviser, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(adminTokens.AccessToken, adviser.PublicId);
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", adviserTokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, adviserTokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Jordan Lee Ltd");
        item.RootElement.GetProperty("adviserId").GetGuid().ShouldBe(adviser.PublicId);
    }

    [Test]
    public async Task Adviser_PutNotAssigned_Returns404()
    {
        var (tenant, _, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var id = await CreateAsync(adminTokens.AccessToken, other.PublicId);
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", adviserTokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion = stale
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();
        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_PutBCustomer_Returns404()
    {
        var (_, _, tokensA) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, adviserB, tokensB) = await CustomerHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example", adviserEmail: "sam@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, adviserB.PublicId);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{idB}", tokensA.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PutOnDisabledTenant_Returns204()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId);
        await DisableTenantAsync(tenant.Id);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await CustomerHttp.Send(
            HttpMethod.Put, $"/users/customers/{id}", tokens.AccessToken, new
            {
                name = "Jordan Lee Ltd",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid adviserId)
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", accessToken, new
            {
                name = "Jordan Lee",
                email = "Jordan@north.example",
                password = "Passw0rd!",
                adviserId
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
        var response = await CustomerHttp.Send(HttpMethod.Get, $"/users/customers/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
