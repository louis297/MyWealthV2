using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class OpenIddictPortalOriginTests
{
    [TearDown]
    public void RestoreFallbackClient()
    {
        using var factory = new IdentityFactory(FunctionalTestSetup.ConnectionString);
        _ = factory.Services;
    }

    [Test]
    public async Task PortalOrigin_RegistersCallbackAndRejectsFallbackRedirect()
    {
        using var factory = CreateIdentity(new Dictionary<string, string>
        {
            ["Identity:PortalOrigin"] = "https://portal.test"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var accepted = await client.GetAsync(Authorize("https://portal.test/signin-oidc"));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        accepted.Headers.Location!.ToString().Contains("/login", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();

        var rejected = await client.GetAsync(Authorize("https://localhost/signin-oidc"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MismatchedRedirect_LogsRegisteredRedirectsAndChallengeUri()
    {
        var logs = new ChallengeLog();
        using var factory = new IdentityFactory(
            FunctionalTestSetup.ConnectionString,
            new Dictionary<string, string>
            {
                ["Identity:PortalOrigin"] = "https://localhost:7190"
            },
            logs);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var rejected = await client.GetAsync(Authorize("http://localhost:5290/signin-oidc"));

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var mismatch = logs.Messages.Single(message => message.Contains("registered redirects", StringComparison.Ordinal));
        mismatch.ShouldContain("http://localhost:5290/signin-oidc");
        mismatch.ShouldContain("https://localhost:7190/signin-oidc");
    }

    [Test]
    public async Task PortalOrigins_RegisterHttpAndHttpsCallbacks()
    {
        using var factory = CreateIdentity(new Dictionary<string, string>
        {
            ["Identity:PortalOrigins:0"] = "http://127.0.0.1:5173",
            ["Identity:PortalOrigins:1"] = "https://127.0.0.1:5173"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        (await client.GetAsync(Authorize("http://127.0.0.1:5173/signin-oidc")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetAsync(Authorize("https://127.0.0.1:5173/signin-oidc")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Test]
    public async Task PortalOrigin_AllowsCorsPreflightFromPortal()
    {
        using var factory = CreateIdentity(new Dictionary<string, string>
        {
            ["Identity:PortalOrigin"] = "https://portal.test"
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/connect/token");
        request.Headers.Add("Origin", "https://portal.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain("https://portal.test");
    }

    private static IdentityFactory CreateIdentity(IReadOnlyDictionary<string, string> settings) =>
        new(FunctionalTestSetup.ConnectionString, settings);

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

    private static string Authorize(string redirectUri) =>
        "/connect/authorize?" + string.Join("&",
        [
            "client_id=adviser-portal",
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "response_type=code",
            "scope=" + Uri.EscapeDataString("openid profile offline_access api"),
            "code_challenge=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG",
            "code_challenge_method=S256"
        ]);
}
