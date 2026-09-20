using System.Net;
using System.Text.Json;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Tenants;

public class DisableEnableTenantTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var disable = await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", accessToken: null, new { rowVersion = "x" });
        disable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        disable.Headers.Location.ShouldBeNull();

        var enable = await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/enable", accessToken: null, new { rowVersion = "x" });
        enable.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        enable.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdmin_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm T", "firmt");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firmt", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firmt", "Password1!", "firmt");
        await AssertForbidden(tenant.PublicId, tokens.AccessToken);
    }

    [Test]
    public async Task Adviser_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm A", "firma");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firma", "Password1!", tenant.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firma", "Password1!", "firma");
        await AssertForbidden(tenant.PublicId, tokens.AccessToken);
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
        await AssertForbidden(tenant.PublicId, tokens.AccessToken);
    }

    [Test]
    public async Task Disable_LeavesPeopleActiveAndRevokesRefresh()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var person = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Ned", "ned@north", "Password1!", tenant.Id);
        var personTokens = await FunctionalTestSetup.Oidc.SignInAsync("ned@north", "Password1!", "north-advisory");
        var admin = await TenantHttp.SignInSystemAdmin();
        var rowVersion = await ReadRowVersion(tenant.PublicId, admin.AccessToken);

        var disable = await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", admin.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadTenant(tenant.PublicId, admin.AccessToken);
        item.RootElement.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        var loaded = await TestApp.FindAsync<User>(person.Id);
        loaded!.Status.ShouldBe(UserStatus.Active);

        var refresh = await FunctionalTestSetup.Oidc.RefreshAsync(personTokens.RefreshToken!);
        refresh.IsSuccessStatusCode.ShouldBeFalse();

        rowVersion = item.RootElement.GetProperty("rowVersion").GetString();
        var second = await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", admin.AccessToken, new { rowVersion });
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Enable_RestoresLoginForActivePeople()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Ned", "ned@north", "Password1!", tenant.Id);
        var personTokens = await FunctionalTestSetup.Oidc.SignInAsync("ned@north", "Password1!", "north-advisory");
        var admin = await TenantHttp.SignInSystemAdmin();

        var rowVersion = await ReadRowVersion(tenant.PublicId, admin.AccessToken);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", admin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var disabled = await ReadTenant(tenant.PublicId, admin.AccessToken);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/enable", admin.AccessToken,
            new { rowVersion = disabled.RootElement.GetProperty("rowVersion").GetString() }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var enabled = await ReadTenant(tenant.PublicId, admin.AccessToken);
        enabled.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        var oldRefresh = await FunctionalTestSetup.Oidc.RefreshAsync(personTokens.RefreshToken!);
        oldRefresh.IsSuccessStatusCode.ShouldBeFalse();

        var login = await FunctionalTestSetup.Oidc.SignInAsync("ned@north", "Password1!", "north-advisory");
        login.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task StaleRowVersion_Returns409()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var admin = await TenantHttp.SignInSystemAdmin();
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", admin.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/enable", admin.AccessToken, new { rowVersion = stale }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task MissingRowVersion_Returns400()
    {
        var tenant = await TestApp.CreateTenantAsync("North Advisory", "north-advisory");
        var admin = await TenantHttp.SignInSystemAdmin();

        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/disable", admin.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenant.PublicId}/enable", admin.AccessToken, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UnknownPublicId_Returns404()
    {
        var admin = await TenantHttp.SignInSystemAdmin();
        var id = Guid.NewGuid();
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{id}/disable", admin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{id}/enable", admin.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task AssertForbidden(Guid tenantId, string accessToken)
    {
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenantId}/disable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(
            HttpMethod.Post, $"/tenants/{tenantId}/enable", accessToken, new { rowVersion = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string> ReadRowVersion(Guid id, string accessToken)
    {
        using var json = await ReadTenant(id, accessToken);
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> ReadTenant(Guid id, string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
