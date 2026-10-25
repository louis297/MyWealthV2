using System.Net;
using System.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeLoginTests
{
    private const string SessionCookieName = "__Host-back-office";

    private static readonly string[] Pages =
    [
        "/",
        "/tenants",
        "/tenants/new",
        "/tenant-admins",
        "/tenant-admins/new",
        "/forbidden"
    ];

    private BackOfficeFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BackOfficeFactory(
            identity.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler());
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

    [TestCaseSource(nameof(Pages))]
    public async Task UnauthenticatedPage_ChallengesHostedLogin(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        location!.AbsolutePath.ShouldContain("/connect/authorize");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe(Services.BackOffice);
        query["code_challenge"].ShouldNotBeNullOrWhiteSpace();
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"].ShouldNotBeNull();
        query["redirect_uri"]!.EndsWith("/signin-oidc", StringComparison.Ordinal).ShouldBeTrue();
        AssertSessionCookieAbsent(response);
    }

    [Test]
    public void DevelopmentCookie_IsHostSessionCookie()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.Cookie.Name.ShouldBe(SessionCookieName);
        options.Cookie.HttpOnly.ShouldBeTrue();
        options.Cookie.Path.ShouldBe("/");
        options.Cookie.Domain.ShouldBeNull();
        options.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
        options.Cookie.MaxAge.ShouldBeNull();
        options.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.SameAsRequest);
    }

    private static void AssertSessionCookieAbsent(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        cookies.ShouldNotContain(cookie =>
            cookie.StartsWith(SessionCookieName, StringComparison.Ordinal));
    }
}
