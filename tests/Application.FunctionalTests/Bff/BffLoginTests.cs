using System.Net;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffLoginTests
{
    private BffFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Login_ChallengesAuthorizeWithClientAndPkce()
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=/customers");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe(Services.AdviserPortal);
        query["code_challenge"].ShouldNotBeNullOrWhiteSpace();
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"].ShouldNotBeNull();
        query["redirect_uri"]!.EndsWith("/signin-oidc", StringComparison.Ordinal).ShouldBeTrue();

        var oidc = _factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var properties = oidc.StateDataFormat.Unprotect(query["state"]!);
        properties.ShouldNotBeNull();
        properties!.RedirectUri.ShouldBe("/customers");

        AssertSecurityHeaders(response);
        AssertSessionCookieAbsent(response);
    }

    [Test]
    public async Task Login_WithoutReturnUrl_ReturnsToRoot()
    {
        var response = await _client.GetAsync("/bff/login");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var state = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"];
        var oidc = _factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidc.StateDataFormat.Unprotect(state!).ShouldNotBeNull().RedirectUri.ShouldBe("/");
        AssertSecurityHeaders(response);
    }

    [TestCase("https://evil.example")]
    [TestCase("//evil.example")]
    [TestCase("/\\evil")]
    [TestCase("/customers\\x")]
    public async Task BadReturnUrl_Is400(string returnUrl)
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=" + Uri.EscapeDataString(returnUrl));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        AssertSecurityHeaders(response);
    }

    [Test]
    public void DevelopmentCookie_IsHostSessionCookie()
    {
        var options = SessionCookie(_factory);

        options.Cookie.Name.ShouldBe("__Host-bff-adviser-portal");
        options.Cookie.HttpOnly.ShouldBeTrue();
        options.Cookie.Path.ShouldBe("/");
        options.Cookie.Domain.ShouldBeNull();
        options.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
        options.Cookie.MaxAge.ShouldBeNull();
        options.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.SameAsRequest);
        options.SessionStore.ShouldBeNull();
    }

    [Test]
    public async Task ProductionCookie_IsSecure()
    {
        using var factory = CreateFactory("Production");
        var options = SessionCookie(factory);
        options.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/bff/login");
        AssertSecurityHeaders(response);
    }

    private static BffFactory CreateFactory(string environment = "Development")
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        var authority = identity.BaseAddress!.ToString();
        var handler = FunctionalTestSetup.Identity.Server.CreateHandler();
        return new BffFactory(authority, handler, environment);
    }

    private static CookieAuthenticationOptions SessionCookie(BffFactory factory) =>
        factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        var frame = response.Headers.GetValues("Content-Security-Policy").Single();
        frame.ShouldBe("frame-ancestors 'none'");
        frame.ShouldNotContain("script-src");
    }

    private static void AssertSessionCookieAbsent(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        cookies.ShouldNotContain(cookie =>
            cookie.StartsWith("__Host-bff-adviser-portal", StringComparison.Ordinal));
    }
}
