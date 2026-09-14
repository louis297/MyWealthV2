using System.Net;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class EndSessionTests : TestBase
{
    private const string RegisteredPostLogout = "https://localhost/";
    private const string UnregisteredPostLogout = "https://evil.example/";

    [Test]
    public async Task RegisteredPostLogoutUri_RedirectsAndClearsHostedLoginCookie()
    {
        using var client = FunctionalTestSetup.Oidc.CreateSessionClient();
        await FunctionalTestSetup.Oidc.SignInAsync(
            client,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var logout = await FunctionalTestSetup.Oidc.EndSessionAsync(client, RegisteredPostLogout);

        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        logout.Headers.Location.ShouldNotBeNull();
        logout.Headers.Location.ToString().ShouldBe(RegisteredPostLogout);

        var authorize = await client.GetAsync(FunctionalTestSetup.Oidc.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location.ShouldNotBeNull();
        var authorizeLocation = authorize.Headers.Location.ToString();
        authorizeLocation.Contains("/login", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        authorizeLocation.Contains("code=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Test]
    public async Task UnregisteredPostLogoutUri_ReturnsInvalidRequest()
    {
        using var client = FunctionalTestSetup.Oidc.CreateSessionClient();

        var logout = await FunctionalTestSetup.Oidc.EndSessionAsync(client, UnregisteredPostLogout);

        logout.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await logout.Content.ReadAsStringAsync();
        body.ShouldContain("invalid_request");
        body.ShouldContain("ID2052");
    }

    [Test]
    public async Task LogoutAndRevocation_MakeRefreshFailAndNextAuthorizeShowsLogin()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm L", "firml");
        await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Lee", "lee@firml", "Password1!", tenant.Id);

        using var client = FunctionalTestSetup.Oidc.CreateSessionClient();
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            client, "lee@firml", "Password1!", "firml");
        tokens.RefreshToken.ShouldNotBeNull();

        var revoke = await FunctionalTestSetup.Oidc.RevokeRefreshAsync(tokens.RefreshToken);
        revoke.IsSuccessStatusCode.ShouldBeTrue();

        var logout = await FunctionalTestSetup.Oidc.EndSessionAsync(client, RegisteredPostLogout);
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var refresh = await FunctionalTestSetup.Oidc.RefreshAsync(tokens.RefreshToken);
        refresh.IsSuccessStatusCode.ShouldBeFalse();

        var authorize = await client.GetAsync(FunctionalTestSetup.Oidc.AuthorizeUrl());
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        authorize.Headers.Location.ShouldNotBeNull();
        authorize.Headers.Location.ToString().Contains("/login", StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue();
    }
}
