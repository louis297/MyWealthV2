extern alias BffHost;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffRefreshTests
{
    private const string SessionCookie = "__Host-bff-adviser-portal";

    private RecordingWebApi _api = null!;
    private TokenExchangeCounter _tokens = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _api = new RecordingWebApi();
        _tokens = new TokenExchangeCounter(FunctionalTestSetup.Identity.Server.CreateHandler());
        _factory = new BffFactory(
            probe.BaseAddress!.ToString(),
            _tokens,
            api: _api,
            apiBaseAddress: FunctionalTestSetup.WebClient.BaseAddress!.ToString());
        _bff = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        _identity = FunctionalTestSetup.Identity.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await RegisterOriginAsync();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        _identity.Dispose();
        _tokens.Dispose();
        await _factory.DisposeAsync();
    }

    [SetUp]
    public void Clear() => _api.Calls.Clear();

    [Test]
    public async Task WebApi401_RefreshesOnce_ThenRetries_AndRetiresThePresentedRefresh()
    {
        var (cookie, refresh) = await SignInAsync();
        refresh.ShouldNotBeNullOrWhiteSpace();
        var refreshesBefore = _tokens.RefreshGrants;
        _api.Next.Enqueue((HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}"""));
        _api.Next.Enqueue((HttpStatusCode.OK, """{"id":"me"}"""));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"id":"me"}""");
        (_tokens.RefreshGrants - refreshesBefore).ShouldBe(1);
        _api.Calls.Count.ShouldBe(2);
        _api.Calls[1].Authorization.ShouldNotBe(_api.Calls[0].Authorization);
        response.Headers.Location.ShouldBeNull();

        var again = await RefreshAsync(refresh);
        again.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Test]
    public async Task FailedRefresh_ClearsTheCookie_AndDoesNotRetryOrAuthorize()
    {
        var (cookie, refresh) = await SignInAsync();
        refresh.ShouldNotBeNullOrWhiteSpace();
        (await RevokeAsync(refresh)).IsSuccessStatusCode.ShouldBeTrue();
        var callsBefore = _api.Calls.Count;
        _api.Next.Enqueue((HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}"""));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        (_api.Calls.Count - callsBefore).ShouldBe(1);
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).ShouldBeTrue();
        setCookies.ShouldContain(value =>
            value.StartsWith(SessionCookie, StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = Services.AdviserPortal,
            ["client_secret"] = AdviserPortalTestSecret.Value
        });
        return await _identity.PostAsync("/connect/token", body);
    }

    private async Task<HttpResponseMessage> RevokeAsync(string refreshToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = Services.AdviserPortal,
            ["client_secret"] = AdviserPortalTestSecret.Value
        });
        return await _identity.PostAsync("/connect/revocation", body);
    }

    private async Task RegisterOriginAsync()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(Services.AdviserPortal)
            ?? throw new InvalidOperationException("adviser-portal is not registered.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = AdviserPortalTestSecret.Value;
        var origin = _bff.BaseAddress!.ToString().TrimEnd('/');
        var redirect = new Uri(origin + "/signin-oidc");
        if (!descriptor.RedirectUris.Contains(redirect))
        {
            descriptor.RedirectUris.Add(redirect);
        }

        var postLogout = new Uri(origin + "/");
        if (!descriptor.PostLogoutRedirectUris.Contains(postLogout))
        {
            descriptor.PostLogoutRedirectUris.Add(postLogout);
        }

        await manager.UpdateAsync(application, descriptor);
    }

    private async Task<(string Cookie, string? Refresh)> SignInAsync()
    {
        var login = await _bff.GetAsync("/bff/login");
        var cookie = CookieHeader(login);
        var callback = await FollowLoginAsync(login.Headers.Location!);
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callback.PathAndQuery);
        callbackRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
        var signedIn = await _bff.SendAsync(callbackRequest);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return (CookieHeader(signedIn), _tokens.LastRefreshToken);
    }

    private async Task<Uri> FollowLoginAsync(Uri authorize)
    {
        var url = authorize.PathAndQuery;
        var prefix = _bff.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            var response = await _identity.GetAsync(url);
            if (response.Headers.Location is { } early)
            {
                var earlyTarget = early.IsAbsoluteUri ? early : new Uri(_identity.BaseAddress!, early);
                if (earlyTarget.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return earlyTarget;
                }
            }

            if ((int)response.StatusCode is < 300 or >= 400)
            {
                throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            }

            var next = response.Headers.Location!;
            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var html = await (await _identity.GetAsync(url)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = DevelopmentIdentitySeeder.SystemAdminEmail,
            ["Password"] = DevelopmentIdentitySeeder.SystemAdminPassword,
            ["TenantCode"] = "",
            ["__RequestVerificationToken"] = token
        });
        var posted = await _identity.PostAsync(url, form);
        for (var i = 0; i < 8; i++)
        {
            var next = posted.Headers.Location ?? throw new InvalidOperationException(await posted.Content.ReadAsStringAsync());
            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        throw new InvalidOperationException("No callback.");
    }

    private static string CookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(value => value.Split(';', 2)[0]))
            : "";

    private sealed class TokenExchangeCounter(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _refreshGrants;

        public int RefreshGrants => _refreshGrants;

        public string? LastRefreshToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.RequestUri?.AbsolutePath.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase) == true &&
                request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (body is null)
            {
                return response;
            }

            if (body.Contains("grant_type=refresh_token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _refreshGrants);
            }

            if (response.IsSuccessStatusCode &&
                body.Contains("grant_type=authorization_code", StringComparison.Ordinal))
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("refresh_token", out var refresh))
                {
                    LastRefreshToken = refresh.GetString();
                }
            }

            return response;
        }
    }
}
