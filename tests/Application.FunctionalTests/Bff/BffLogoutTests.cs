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

public class BffLogoutTests
{
    private const string SessionCookie = "__Host-bff-adviser-portal";

    private RevocationCounter _revocations = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _revocations = new RevocationCounter(FunctionalTestSetup.Identity.Server.CreateHandler());
        _factory = new BffFactory(probe.BaseAddress!.ToString(), _revocations, api: new RecordingWebApi());
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
        _revocations.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Logout_WithoutHeader_Is400AndDoesNotRevoke()
    {
        var (cookie, _) = await SignInAsync();
        var before = _revocations.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _revocations.Count.ShouldBe(before);
    }

    [Test]
    public async Task Logout_RevokesThenRedirectsToEndSession_AndClearsTheCookie()
    {
        var (cookie, refresh) = await SignInAsync();
        refresh.ShouldNotBeNullOrWhiteSpace();
        var before = _revocations.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("X-MyWealth-Request", "1");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.ShouldContain("/connect/logout");
        location.ShouldContain("client_id=adviser-portal");
        location.ShouldContain("post_logout_redirect_uri=");
        (_revocations.Count - before).ShouldBe(1);
        response.Headers.GetValues("Set-Cookie").ShouldContain(value =>
            value.StartsWith(SessionCookie, StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        var again = await RefreshAsync(refresh!);
        again.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Test]
    public async Task GetLogout_Is405()
    {
        var response = await _bff.GetAsync("/bff/logout");
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task Continue_RedirectsToEndSession_WithoutRevokingAgain()
    {
        var before = _revocations.Count;
        var response = await _bff.GetAsync("/bff/logout/continue");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/connect/logout");
        _revocations.Count.ShouldBe(before);
    }

    [Test]
    public async Task Logout_WhenAlreadySignedOut_RedirectsWithoutRevocation()
    {
        var before = _revocations.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.TryAddWithoutValidation("X-MyWealth-Request", "1");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/connect/logout");
        _revocations.Count.ShouldBe(before);
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
        if (!descriptor.RedirectUris.Contains(new Uri(origin + "/signin-oidc")))
        {
            descriptor.RedirectUris.Add(new Uri(origin + "/signin-oidc"));
        }

        if (!descriptor.PostLogoutRedirectUris.Contains(new Uri(origin + "/")))
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(origin + "/"));
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
        return (CookieHeader(signedIn), _revocations.LastRefreshToken);
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

    private sealed class RevocationCounter(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _count;

        public int Count => _count;

        public string? LastRefreshToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null &&
                request.RequestUri?.AbsolutePath is "/connect/token" or "/connect/revocation")
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
                request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            }

            if (request.RequestUri?.AbsolutePath == "/connect/revocation")
            {
                Interlocked.Increment(ref _count);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (body is not null &&
                response.IsSuccessStatusCode &&
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
