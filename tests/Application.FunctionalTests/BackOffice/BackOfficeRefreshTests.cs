extern alias BackOfficeHost;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
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
            new HttpClient(tokenEndpoint) { BaseAddress = new Uri("http://identity") },
            store,
            BackOfficeTestSecret.Value);

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

    private static BackOfficeTokenRefresh NewRefresh(ScriptedHandler tokenEndpoint, DateTimeOffset utcNow) =>
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

    private sealed class MemoryAccessTokens(string? access, string? refresh) : IAccessTokens
    {
        public string? AccessToken { get; private set; } = access;

        public string? RefreshToken { get; private set; } = refresh;

        public void Store(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
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
