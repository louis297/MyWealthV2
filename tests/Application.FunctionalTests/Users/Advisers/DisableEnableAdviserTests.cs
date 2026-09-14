using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.Advisers;

public class DisableEnableAdviserTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var disable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{Guid.NewGuid()}/disable", accessToken: null,
            new { rowVersion = "x" });
        disable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        disable.Headers.Location.ShouldBeNull();

        var enable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{Guid.NewGuid()}/enable", accessToken: null,
            new { rowVersion = "x" });
        enable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        enable.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await AdviserHttp.SignInSystemAdmin();
        await AssertForbidden(Guid.NewGuid(), tokens.AccessToken);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");
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
    public async Task Disable_SetsDisabledAndRevokesRefresh()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var personTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ned@north.example", "Passw0rd!", "north-advisory");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("disabled");

        var refresh = await FunctionalTestSetup.Oidc.RefreshAsync(personTokens.RefreshToken!);
        refresh.IsSuccessStatusCode.ShouldBeFalse();

        rowVersion = item.RootElement.GetProperty("rowVersion").GetString();
        var second = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion });
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Enable_RestoresActive()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var enabled = await ReadItem(id, tokens.AccessToken);
        enabled.RootElement.GetProperty("status").GetString().ShouldBe("active");

        var alreadyActive = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken,
            new { rowVersion = enabled.RootElement.GetProperty("rowVersion").GetString() });
        alreadyActive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DisableLastAdviserWithNoCustomers_Returns204()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DisableWhileAssignedCustomerActive_Returns400AndStaysActive()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var adviser = await FindPersonAsync(id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@north.example", "Password1!", tenant.Id, adviser!.Id);
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await disable.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("code", out var code).ShouldBeFalse();
        code.ValueKind.ShouldBe(JsonValueKind.Undefined);
        json.RootElement.TryGetProperty("target", out _).ShouldBeFalse();
        json.RootElement.GetProperty("errors").ToString()
            .ShouldContain("Reassign or disable assigned customers before disabling this adviser.");

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("active");
    }

    [Test]
    public async Task DisableAfterAssignedCustomersDisabled_Returns204()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var adviser = await FindPersonAsync(id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@north.example", "Password1!", tenant.Id, adviser!.Id);
        await DisablePersonAsync(customer.PublicId);

        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);
        var disable = await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("disabled");
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");

        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var (_, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = Guid.NewGuid();
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TenantAdminOfA_DisableBAdviser_Returns404()
    {
        var (_, tokensA) = await AdviserHttp.SignInTenantAdmin(
            "North Advisory", "north-advisory", "tess@north.example");
        var (_, tokensB) = await AdviserHttp.SignInTenantAdmin(
            "South Co", "south-co", "tess@south.example");
        var idB = await CreateAsync(tokensB.AccessToken, "Blair Ng", "blair@south.example");
        var rowVersion = await ReadRowVersion(idB, tokensB.AccessToken);

        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{idB}/disable", tokensA.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{idB}/enable", tokensA.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisableEnableOnDisabledTenant_Returns204()
    {
        var (tenant, tokens) = await AdviserHttp.SignInTenantAdmin();
        var id = await CreateAsync(tokens.AccessToken, "Ned North", "ned@north.example");
        await DisableTenantAsync(tenant.Id);

        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AssertForbidden(Guid id, string accessToken)
    {
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AdviserHttp.Send(
            HttpMethod.Post, $"/users/advisers/{id}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

    private static async Task<User?> FindPersonAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.SingleOrDefaultAsync(person => person.PublicId == publicId);
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
