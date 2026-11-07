extern alias BackOfficeHost;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeRefreshTests
{
    [Test]
    public async Task WebApi401_RefreshesOnce_AndRetriesWithTheNewAccessToken()
    {
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        var store = new MemoryAccessTokens("old-access", "old-refresh");
        var platform = new PlatformApi(
            new HttpClient(api) { BaseAddress = new Uri("http://webapi") },
            store,
            NewRefresh(tokenEndpoint, new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)));

        var body = await platform.ListTenantsAsync();

        body.ShouldContain("\"items\"");
        api.Requests.Count.ShouldBe(2);
        api.Requests[0].Authorization.ShouldBe("Bearer old-access");
        api.Requests[1].Authorization.ShouldBe("Bearer next-access");
        tokenEndpoint.Requests.Count.ShouldBe(1);
        var form = HttpUtility.ParseQueryString(tokenEndpoint.Requests[0].Body);
        form["grant_type"].ShouldBe("refresh_token");
        form["refresh_token"].ShouldBe("old-refresh");
        form["client_id"].ShouldBe(Services.BackOffice);
        form["client_secret"].ShouldBe(BackOfficeTestSecret.Value);
        store.AccessToken.ShouldBe("next-access");
        store.RefreshToken.ShouldBe("next-refresh");
    }

    [Test]
    public async Task WebApi401_AndTheNextCookieValidation_ExchangeTheRefreshTokenOnce()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var api = new ScriptedHandler();
        api.Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        api.Enqueue(HttpStatusCode.OK, """{"items":[],"page":1,"pageSize":20,"totalCount":0}""");
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        var refresh = NewRefresh(tokenEndpoint, now);
        var store = new MemoryAccessTokens("old-access", "old-refresh");
        var platform = new PlatformApi(
            new HttpClient(api) { BaseAddress = new Uri("http://webapi") },
            store,
            refresh);

        var body = await platform.ListTenantsAsync();
        var context = Principal("old-access", "old-refresh", now.AddMinutes(-1), "id-1");
        await refresh.ValidatePrincipalAsync(context);

        body.ShouldContain("\"items\"");
        api.Requests[1].Authorization.ShouldBe("Bearer next-access");
        tokenEndpoint.Requests.Count.ShouldBe(1);
        context.Properties.GetTokenValue("access_token").ShouldBe("next-access");
        context.Properties.GetTokenValue("refresh_token").ShouldBe("next-refresh");
    }

    [Test]
    public void CookieValidation_RefreshesExpiredAccessTokens()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        using var factory = new BackOfficeFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler());
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var declaringType = options.Events.OnValidatePrincipal.Method.DeclaringType!.FullName ?? "";
        declaringType.ShouldContain(nameof(BackOfficeAuthenticationExtensions));
    }

    [Test]
    public async Task ExpiredAccessToken_RenewsTheCookieWithTheNewTokens()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        var refresh = NewRefresh(tokenEndpoint, now);
        var context = Principal("old-access", "old-refresh", now.AddMinutes(-1), "id-1");

        await refresh.ValidatePrincipalAsync(context);

        context.ShouldRenew.ShouldBeTrue();
        context.Principal!.Identity!.IsAuthenticated.ShouldBeTrue();
        context.Properties.GetTokenValue("access_token").ShouldBe("next-access");
        context.Properties.GetTokenValue("refresh_token").ShouldBe("next-refresh");
        context.Properties.GetTokenValue("id_token").ShouldBe("id-1");
        DateTimeOffset.Parse(
                context.Properties.GetTokenValue("expires_at")!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind)
            .ShouldBe(now.AddSeconds(900));
        tokenEndpoint.Requests.Count.ShouldBe(1);
        var form = HttpUtility.ParseQueryString(tokenEndpoint.Requests[0].Body);
        form["grant_type"].ShouldBe("refresh_token");
        form["refresh_token"].ShouldBe("old-refresh");
        form["client_id"].ShouldBe(Services.BackOffice);
        form["client_secret"].ShouldBe(BackOfficeTestSecret.Value);
    }

    [Test]
    public async Task StillValidAccessToken_DoesNotRefresh()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var tokenEndpoint = new ScriptedHandler();
        var refresh = NewRefresh(tokenEndpoint, now);
        var context = Principal("current-access", "current-refresh", now.AddMinutes(10), "id-1");

        await refresh.ValidatePrincipalAsync(context);

        context.ShouldRenew.ShouldBeFalse();
        context.Properties.GetTokenValue("access_token").ShouldBe("current-access");
        context.Properties.GetTokenValue("refresh_token").ShouldBe("current-refresh");
        tokenEndpoint.Requests.Count.ShouldBe(0);
    }

    [Test]
    public async Task SecondPresentationOfARotatedRefreshToken_DoesNotCallTheTokenEndpoint()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        tokenEndpoint.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var refresh = NewRefresh(tokenEndpoint, now);

        await refresh.ValidatePrincipalAsync(Principal("old-access", "old-refresh", now.AddMinutes(-1), "id-1"));
        var again = Principal("old-access", "old-refresh", now.AddMinutes(-1), "id-1");
        await refresh.ValidatePrincipalAsync(again);

        tokenEndpoint.Requests.Count.ShouldBe(1);
        again.ShouldRenew.ShouldBeTrue();
        again.Properties.GetTokenValue("access_token").ShouldBe("next-access");
        again.Properties.GetTokenValue("refresh_token").ShouldBe("next-refresh");
    }

    [Test]
    public async Task ConcurrentPresentations_ExchangeTheRefreshTokenOnce()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var tokenEndpoint = new BlockingHandler();
        var refresh = NewRefresh(tokenEndpoint, now);

        var first = refresh.ExchangeAsync("old-refresh");
        var second = refresh.ExchangeAsync("old-refresh");
        var results = await Task.WhenAll(first, second);

        tokenEndpoint.Entered.ShouldBe(1);
        results[0]!.AccessToken.ShouldBe("next-access");
        results[1]!.RefreshToken.ShouldBe("next-refresh");
    }

    [Test]
    public async Task ExpiredCachedAccessToken_PresentsTheLatestRefreshToken()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTime(now);
        var tokenEndpoint = new ScriptedHandler();
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""");
        tokenEndpoint.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"later-access","refresh_token":"later-refresh","expires_in":900}""");
        var refresh = new BackOfficeTokenRefresh(
            new HttpClient(tokenEndpoint),
            () => "http://identity/connect/token",
            BackOfficeTestSecret.Value,
            clock);

        var first = await refresh.ExchangeAsync("old-refresh");
        first!.RefreshToken.ShouldBe("next-refresh");
        clock.UtcNow = now.AddMinutes(20);

        var second = await refresh.ExchangeAsync("old-refresh");

        second!.AccessToken.ShouldBe("later-access");
        second.RefreshToken.ShouldBe("later-refresh");
        tokenEndpoint.Requests.Count.ShouldBe(2);
        var form = HttpUtility.ParseQueryString(tokenEndpoint.Requests[1].Body);
        form["refresh_token"].ShouldBe("next-refresh");
    }

    [Test]
    public void Store_AfterTheResponseStarted_KeepsTheNewAccessToken()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(new StartedResponse());
        http.Response.HasStarted.ShouldBeTrue();
        var tokens = new CookieAccessTokens(new ContextAccessor(http));

        tokens.Store(
            "next-access",
            "next-refresh",
            new DateTimeOffset(2026, 11, 7, 12, 15, 0, TimeSpan.Zero));

        tokens.AccessToken.ShouldBe("next-access");
        tokens.RefreshToken.ShouldBe("next-refresh");
    }

    private static BackOfficeTokenRefresh NewRefresh(HttpMessageHandler tokenEndpoint, DateTimeOffset utcNow) =>
        new(
            new HttpClient(tokenEndpoint) { BaseAddress = new Uri("http://identity/") },
            () => "http://identity/connect/token",
            BackOfficeTestSecret.Value,
            new FixedTime(utcNow));

    private static CookieValidatePrincipalContext Principal(
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        string idToken)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = accessToken },
            new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
            new AuthenticationToken { Name = "id_token", Value = idToken },
            new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("o", CultureInfo.InvariantCulture) }
        ]);
        var identity = new ClaimsIdentity(
            [new Claim("role", "systemAdmin"), new Claim("sub", "admin")],
            authenticationType: "Cookies");
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(
            new DefaultHttpContext(),
            scheme,
            new CookieAuthenticationOptions(),
            ticket);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StartedResponse : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class ContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class ManualTime(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _entered;

        public int Entered => _entered;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _entered);
            if (count == 1)
            {
                await Task.Delay(200, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"next-access","refresh_token":"next-refresh","expires_in":900}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class MemoryAccessTokens(string? access, string? refresh) : IAccessTokens
    {
        public string? AccessToken { get; private set; } = access;

        public string? RefreshToken { get; private set; } = refresh;

        public void Store(string accessToken, string refreshToken, DateTimeOffset expiresAt)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            _ = expiresAt;
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                body));
            var next = _responses.Dequeue();
            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string? Authorization, string Body);
}
