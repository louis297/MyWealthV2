using System.Net;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public async Task Login_ForwardedHttpsHost_IsAuthorizeRedirectAndCorrelationCookie()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/login?returnUrl=/customers");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            "bff-adviser-portal-mywealthv2.dev.localhost:7190");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var redirect = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["redirect_uri"];
        redirect.ShouldNotBeNull();
        redirect.ShouldStartWith("https://");
        redirect.ShouldEndWith("/signin-oidc");
        redirect.ShouldNotContain("dev.localhost");
        redirect.ShouldNotContain(":5290");

        var correlation = response.Headers.GetValues("Set-Cookie").Single(cookie =>
            cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));
        correlation.Contains("secure", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        correlation.Contains("domain=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        AssertSessionCookieAbsent(response);
    }

    [Test]
    public async Task PublicOrigin_LoginAndPostLogoutUseTheHttpsEntry()
    {
        const string entry = "https://localhost:7190";
        var logs = new ChallengeLog();
        using var factory = CreateFactory(publicOrigin: entry, logs: logs);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var login = new HttpRequestMessage(HttpMethod.Get, "/bff/login?returnUrl=/customers");
        login.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        login.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            "bff-adviser-portal-mywealthv2.dev.localhost:7190");

        var response = await client.SendAsync(login);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        HttpUtility.ParseQueryString(response.Headers.Location!.Query)["redirect_uri"]
            .ShouldBe(entry + "/signin-oidc");
        var mismatch = logs.Messages.Single(message => message.Contains("registered redirects", StringComparison.Ordinal));
        var challenge = mismatch.Split(" differs ", 2)[0];
        challenge.ShouldContain("/signin-oidc");
        challenge.ShouldNotContain(entry + "/signin-oidc");
        mismatch.ShouldContain(entry + "/signin-oidc");

        var session = SessionCookie(factory);
        session.Cookie.Name.ShouldBe("__Host-bff-adviser-portal");
        session.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
        session.Cookie.Domain.ShouldBeNull();

        var logout = await client.GetAsync("/bff/logout/continue");
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        HttpUtility.ParseQueryString(logout.Headers.Location!.Query)["post_logout_redirect_uri"]
            .ShouldBe(entry + "/");
    }

    [Test]
    public async Task Production_IgnoresForwardedHttpsHost()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/login");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            "bff-adviser-portal-mywealthv2.dev.localhost:7190");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var redirect = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["redirect_uri"];
        redirect.ShouldNotBeNull();
        redirect!.Contains("dev.localhost", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        redirect.StartsWith("https://", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
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

    private static BffFactory CreateFactory(
        string environment = "Development",
        string? publicOrigin = null,
        ILoggerProvider? logs = null)
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        var authority = identity.BaseAddress!.ToString();
        var handler = FunctionalTestSetup.Identity.Server.CreateHandler();
        return new BffFactory(authority, handler, environment, publicOrigin: publicOrigin, logs: logs);
    }

    private sealed class ChallengeLog : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
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
