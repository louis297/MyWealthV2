using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Customers;

public class DisableEnableCustomerTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var disable = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{Guid.NewGuid()}/disable", accessToken: null,
            new { rowVersion = "x" });
        disable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        disable.Headers.Location.ShouldBeNull();

        var enable = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{Guid.NewGuid()}/enable", accessToken: null,
            new { rowVersion = "x" });
        enable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        enable.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await CustomerHttp.SignInSystemAdmin();
        await AssertForbidden(Guid.NewGuid(), tokens.AccessToken);
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
        await AssertForbidden(Guid.NewGuid(), tokens.AccessToken);
    }

    [Test]
    public async Task Disable_SetsDisabled()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("disabled");
        item.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await FunctionalTestSetup.Oidc.SubmitLoginAsync(
            "ned@north.example", "Passw0rd!", "north-advisory")).Code.ShouldBeNull();

        rowVersion = item.RootElement.GetProperty("rowVersion").GetString();
        var second = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion });
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Enable_RestoresActive()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var enabled = await ReadItem(id, tokens.AccessToken);
        enabled.RootElement.GetProperty("status").GetString().ShouldBe("active");
        enabled.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        var alreadyActive = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken,
            new { rowVersion = enabled.RootElement.GetProperty("rowVersion").GetString() });
        alreadyActive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Adviser_DisableEnableAssigned_Returns204()
    {
        var (_, adviser, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(adminTokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");
        var rowVersion = await ReadRowVersion(id, adviserTokens.AccessToken);

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", adviserTokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, adviserTokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", adviserTokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Adviser_DisableNotAssigned_Returns404()
    {
        var (tenant, _, adminTokens) = await CustomerHttp.SignInTenantAdmin();
        var other = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var id = await CreateAsync(adminTokens.AccessToken, other.PublicId, "Ned North", "ned@north.example");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "sam@north.example", "Password1!", "north-advisory");
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", adviserTokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", adviserTokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, _, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = Guid.NewGuid();
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_DisableBCustomer_Returns404()
    {
        var (_, _, tokensA) = await CustomerHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, adviserB, tokensB) = await CustomerHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example", adviserEmail: "sam@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, adviserB.PublicId, "Blair Ng", "blair@south.example");
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{idB}/disable", tokensA.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{idB}/enable", tokensA.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisableEnableOnDisabledTenant_Returns204()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        await DisableTenantAsync(tenant.Id);

        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AssertForbidden(Guid id, string accessToken)
    {
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{id}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid adviserId, string name, string email)
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

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
