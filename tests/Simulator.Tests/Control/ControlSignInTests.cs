using System.Net;
using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Control;

/// <summary>Simulator spec R16–R19: the control surface is behind a SimulatorOperator session.</summary>
public class ControlSignInTests
{
    private SimulatorFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new SimulatorFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    [Test]
    public async Task ControlRoute_WithoutSession_Returns401WithoutRedirect()
    {
        using var client = _factory.ControlClient();

        var response = await client.GetAsync("/control/world");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [TestCase("/")]
    [TestCase("/scalar")]
    [TestCase("/openapi/v1.json")]
    public async Task Page_WithoutSession_RedirectsToSignInOnIdentity(string path)
    {
        using var client = _factory.ControlClient();

        var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(SimulatorFactory.Authority + "/connect/authorize");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe("simulator");
        query["scope"]!.Split(' ').ShouldBe(["openid", "profile", "email"], ignoreOrder: true);
        query["code_challenge_method"].ShouldBe("S256");
    }

    [Test]
    public async Task OperatorSession_ReachesControlRoutesAndScalar()
    {
        using var client = _factory.SessionClient();

        (await client.GetAsync("/control/world")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/scalar")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var root = await client.GetAsync("/");
        root.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        root.Headers.Location!.ToString().ShouldBe("/scalar");
    }

    [TestCase("systemAdmin")]
    [TestCase("tenantAdmin")]
    [TestCase("customer")]
    public async Task OtherRoleSession_HasNoAccess(string role)
    {
        using var client = _factory.SessionClient("someone@sim.test", role);

        (await client.GetAsync("/control/world")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var page = await client.GetAsync("/scalar");
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page.Headers.Location!.AbsolutePath.ShouldBe("/not-allowed");
    }

    [Test]
    public async Task NotAllowedPage_OffersASignOutOnIdentity()
    {
        using var client = _factory.ControlClient();

        var response = await client.GetAsync("/not-allowed");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain(SimulatorFactory.Authority + "/connect/logout?client_id=simulator");
    }

    [Test]
    public async Task SignOut_ClearsTheSessionAndEndsTheSessionOnIdentity()
    {
        using var client = _factory.SessionClient();

        var response = await client.PostAsync("/signout", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(SimulatorFactory.Authority + "/connect/logout");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe("simulator");
        query["post_logout_redirect_uri"].ShouldBe(SimulatorFactory.PublicOrigin + "/");
        string.Join('\n', response.Headers.GetValues("Set-Cookie")).ShouldContain(SimulatorAuthentication.SessionCookieName + "=;");
    }

    [Test]
    public async Task ControlRoute_SendsNoCorsHeaders()
    {
        using var client = _factory.SessionClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/control/world");
        request.Headers.Add("Origin", "https://evil.test");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Test]
    public void Session_IsAnEightHourCookieWithNoTokens()
    {
        var cookie = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookie.ExpireTimeSpan.ShouldBe(TimeSpan.FromHours(8));
        cookie.SlidingExpiration.ShouldBeFalse();
        cookie.Cookie.Name.ShouldBe(SimulatorAuthentication.SessionCookieName);
        cookie.Cookie.HttpOnly.ShouldBeTrue();
        cookie.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);

        var oidc = _factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidc.ClientId.ShouldBe("simulator");
        oidc.SaveTokens.ShouldBeFalse();
        oidc.UsePkce.ShouldBeTrue();
        oidc.Scope.ShouldBe(["openid", "profile", "email"], ignoreOrder: true);
    }

    [TestCase("systemAdmin")]
    [TestCase("adviser")]
    [TestCase(null)]
    public async Task TokenValidated_ForAnotherRole_IssuesNoSession(string? role)
    {
        var context = TokenValidated(role);

        await SimulatorAuthentication.RejectOtherRolesAsync(context);

        context.Result.ShouldNotBeNull();
        context.Result.Handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().ShouldBe("/not-allowed");
    }

    [Test]
    public async Task TokenValidated_ForTheOperator_IssuesTheSession()
    {
        var context = TokenValidated("simulatorOperator");

        await SimulatorAuthentication.RejectOtherRolesAsync(context);

        context.Result.ShouldBeNull();
    }

    private TokenValidatedContext TokenValidated(string? role)
    {
        var http = new DefaultHttpContext { RequestServices = _factory.Services };
        var claims = new List<Claim> { new("sub", "x"), new("email", "someone@sim.test") };
        if (role is not null)
        {
            claims.Add(new Claim("role", role));
        }

        var options = _factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler));
        return new TokenValidatedContext(
            http,
            scheme,
            options,
            new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")),
            new AuthenticationProperties());
    }
}
