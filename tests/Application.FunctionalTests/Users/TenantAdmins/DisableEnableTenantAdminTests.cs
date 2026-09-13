using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Users.TenantAdmins;

public class DisableEnableTenantAdminTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var disable = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{Guid.NewGuid()}/disable", accessToken: null,
            new { rowVersion = "x" });
        disable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        disable.Headers.Location.ShouldBeNull();

        var enable = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{Guid.NewGuid()}/enable", accessToken: null,
            new { rowVersion = "x" });
        enable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        enable.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        var person = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");
        await AssertForbidden(person.PublicId, tokens.AccessToken);
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
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("dee@firmc", "Password1!", "firmc");
        await AssertForbidden(Guid.NewGuid(), tokens.AccessToken);
    }

    [Test]
    public async Task Disable_SetsDisabledAndRevokesRefresh()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");
        var personTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ned@north.example", "Passw0rd!", "north-advisory");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(id, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("disabled");

        var refresh = await FunctionalTestSetup.Oidc.RefreshAsync(personTokens.RefreshToken!);
        refresh.IsSuccessStatusCode.ShouldBeFalse();

        rowVersion = item.RootElement.GetProperty("rowVersion").GetString();
        var second = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion });
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Enable_RestoresActive()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var enabled = await ReadItem(id, tokens.AccessToken);
        enabled.RootElement.GetProperty("status").GetString().ShouldBe("active");

        var alreadyActive = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken,
            new { rowVersion = enabled.RootElement.GetProperty("rowVersion").GetString() });
        alreadyActive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DisableLastTenantAdmin_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");
        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);

        var disable = await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");

        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = Guid.NewGuid();
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisableEnableOnDisabledTenant_Returns204()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var tokens = await TenantAdminHttp.SignInSystemAdmin();
        var id = await CreateAsync(tenant.PublicId, tokens.AccessToken, "Ned North", "ned@north.example");
        await DisableTenantAsync(tenant.Id);

        var rowVersion = await ReadRowVersion(id, tokens.AccessToken);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadItem(id, tokens.AccessToken);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", tokens.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task AssertForbidden(Guid id, string accessToken)
    {
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantAdminHttp.Send(
            HttpMethod.Post, $"/users/tenant-admins/{id}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> CreateAsync(Guid tenantId, string accessToken, string name, string email)
    {
        var response = await TenantAdminHttp.Send(
            HttpMethod.Post, "/users/tenant-admins", accessToken, new
            {
                tenantId,
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
