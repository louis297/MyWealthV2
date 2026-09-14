using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

public class UpdateAdviserTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{Guid.NewGuid()}", accessToken: null, new
            {
                name = "Samantha Reed",
                rowVersion = "x"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await AdviserHttp.SignInSystemAdmin();

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
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

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken, new
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

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Dee Ltd",
                rowVersion = "x"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_Renames_Returns204()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("name").GetString().ShouldBe("Samantha Reed");
        item.RootElement.GetProperty("email").GetString().ShouldBe("Sam@north.example");
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
    }

    [Test]
    public async Task PutWithEmail_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                email = "other@north.example",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithTenantId_Returns400()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                tenantId = tenant.PublicId,
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutWithAdviserId_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                adviserId = Guid.NewGuid(),
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed"
            });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                rowVersion = stale
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{Guid.NewGuid()}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_PutBAdviser_Returns404()
    {
        var (_, tokensA) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await AdviserHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var idB = await CreateAsync(tokensB.AccessToken);
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{idB}", tokensA.AccessToken, new
            {
                name = "Samantha Reed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PutOnDisabledTenant_Returns204()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken);
        await DisableTenantAsync(tenant.Id);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var response = await AdviserHttp.Send(
            HttpMethod.Put, $"/users/advisers/{id}", tokens.AccessToken, new
            {
                name = "Samantha Reed",
                rowVersion
            });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> CreateAsync(string accessToken)
    {
        var response = await AdviserHttp.Send(
            HttpMethod.Post, "/users/advisers", accessToken, new
            {
                name = "Sam Reed",
                email = "Sam@north.example",
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
        var response = await AdviserHttp.Send(HttpMethod.Get, $"/users/advisers/{id}", accessToken);
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
