using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeSessionTests
{
    private const string SessionCookieName = "__Host-back-office";

    private BackOfficeFactory _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BackOfficeFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
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
        _client.Dispose();
        _identity.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task SystemAdmin_WithoutTenantCode_LandsOnHome()
    {
        var challenge = await _client.GetAsync("/");
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var correlation = CookieHeader(challenge);

        var callback = await SignInUntilCallbackAsync(challenge.Headers.Location!);
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callback.PathAndQuery);
        callbackRequest.Headers.TryAddWithoutValidation("Cookie", correlation);
        var afterCallback = await _client.SendAsync(callbackRequest);
        var jar = MergeCookies(correlation, afterCallback);
        jar.ShouldContain(SessionCookieName + "=");

        var page = await FollowToPageAsync(afterCallback, jar);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await page.Content.ReadAsStringAsync();
        body.ShouldContain("href=\"/tenants\"");
        body.ShouldContain("href=\"/tenant-admins\"");
        body.ShouldNotContain("access_token");
        body.ShouldNotContain("refresh_token");
        if (page.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                cookie.ShouldNotContain("access_token");
                cookie.ShouldNotContain("refresh_token");
            }
        }
    }

    private async Task RegisterOriginAsync()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(Services.BackOffice)
            ?? throw new InvalidOperationException("back-office is not registered.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = BackOfficeTestSecret.Value;
        var origin = _client.BaseAddress!.ToString().TrimEnd('/');
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
        var callbackPrefix = _client.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
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

    private async Task<HttpResponseMessage> FollowToPageAsync(HttpResponseMessage response, string cookies)
    {
        for (var i = 0; i < 4; i++)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response;
            }

            if (response.Headers.Location is not { } next)
            {
                return response;
            }

            cookies = MergeCookies(cookies, response);
            var target = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            response.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
            response = await _client.SendAsync(request);
        }

        return response;
    }

    private static string MergeCookies(string existing, HttpResponseMessage response)
    {
        var jar = existing.Split("; ", StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=', 2)[0])
            .Zip(existing.Split("; ", StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0];
                var name = pair.Split('=', 2)[0];
                jar[name] = pair;
            }
        }

        return string.Join("; ", jar.Values);
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return "";
        }

        return string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
    }
}
