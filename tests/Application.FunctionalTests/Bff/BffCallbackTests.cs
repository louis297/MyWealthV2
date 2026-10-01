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

public class BffCallbackTests
{
    private const string SessionCookieName = "__Host-bff-adviser-portal";

    private CountingHandler _tokens = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        var authority = probe.BaseAddress!;
        _tokens = new CountingHandler(FunctionalTestSetup.Identity.Server.CreateHandler());
        _factory = new BffFactory(authority.ToString(), _tokens);
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
        await RegisterBffOriginAsync();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        _identity.Dispose();
        _tokens.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Callback_RedeemsACodeOnce_AndHidesTokens()
    {
        var login = await _bff.GetAsync("/bff/login?returnUrl=/customers");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cookie = CookieHeader(login);

        var callback = await SignInUntilCallbackAsync(login.Headers.Location!);
        var code = HttpUtility.ParseQueryString(callback.Query)["code"];
        code.ShouldNotBeNullOrWhiteSpace();

        var path = callback.PathAndQuery;
        var first = new HttpRequestMessage(HttpMethod.Get, path);
        var second = new HttpRequestMessage(HttpMethod.Get, path);
        first.Headers.TryAddWithoutValidation("Cookie", cookie);
        second.Headers.TryAddWithoutValidation("Cookie", cookie);

        var responses = await Task.WhenAll(_bff.SendAsync(first), _bff.SendAsync(second));
        _tokens.AuthorizationCodeGrants.ShouldBe(1);

        var accessToken = _tokens.AccessTokens.Single();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldNotContain("access_token");
            body.ShouldNotContain("refresh_token");
            body.ShouldNotContain(accessToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    setCookie.ShouldNotContain(accessToken);
                }
            }
        }

        var sessionCookies = responses
            .SelectMany(response => response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            .Where(value => value.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
            .ToArray();
        sessionCookies.Length.ShouldBeGreaterThan(0);
        foreach (var session in sessionCookies)
        {
            session.Contains("domain=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
            session.Contains("expires=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
            session.Contains("max-age=", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }

        responses.Any(response => response.StatusCode == HttpStatusCode.Redirect).ShouldBeTrue();
    }

    private async Task RegisterBffOriginAsync()
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
        var postLogout = new Uri(origin + "/");
        if (!descriptor.RedirectUris.Contains(redirect))
        {
            descriptor.RedirectUris.Add(redirect);
        }

        if (!descriptor.PostLogoutRedirectUris.Contains(postLogout))
        {
            descriptor.PostLogoutRedirectUris.Add(postLogout);
        }

        await manager.UpdateAsync(application, descriptor);
    }

    private async Task<Uri> SignInUntilCallbackAsync(Uri authorize)
    {
        var location = await FollowToLoginAsync(authorize);
        var loginHtml = await (await _identity.GetAsync(location)).Content.ReadAsStringAsync();
        var token = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(loginHtml, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = DevelopmentIdentitySeeder.SystemAdminEmail,
            ["Password"] = DevelopmentIdentitySeeder.SystemAdminPassword,
            ["TenantCode"] = "",
            ["__RequestVerificationToken"] = token
        });

        var response = await _identity.PostAsync(location, form);
        var callbackPrefix = _bff.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            if (response.Headers.Location is not { } next)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Login did not return a code. {(int)response.StatusCode} {body}");
            }

            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(callbackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            response.Dispose();
            response = await _identity.GetAsync(target);
        }

        throw new InvalidOperationException("Authorize did not return a code.");
    }

    private async Task<string> FollowToLoginAsync(Uri authorize)
    {
        var url = authorize.IsAbsoluteUri ? authorize.PathAndQuery : authorize.ToString();
        for (var i = 0; i < 8; i++)
        {
            var response = await _identity.GetAsync(url);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Authorize did not reach hosted login. {(int)response.StatusCode} {body}");
            }

            var next = response.Headers.Location ?? throw new InvalidOperationException("Authorize redirect had no Location.");
            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        throw new InvalidOperationException("Did not reach hosted login.");
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return "";
        }

        return string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
    }

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _authorizationCodeGrants;

        public int AuthorizationCodeGrants => _authorizationCodeGrants;

        public List<string> AccessTokens { get; } = [];

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
            if (body is null || !body.Contains("grant_type=authorization_code", StringComparison.Ordinal))
            {
                return response;
            }

            Interlocked.Increment(ref _authorizationCodeGrants);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            response.Content = new StringContent(json, Encoding.UTF8, mediaType);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("access_token", out var access))
                {
                    AccessTokens.Add(access.GetString()!);
                }
            }

            return response;
        }
    }
}
