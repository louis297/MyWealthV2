extern alias BffCustomer;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWealthV2.Domain.Enums;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.BffCustomer;

public class CustomerBffTests : TestBase
{
    private const string SessionCookie = "__Host-bff-customer-portal";

    private CustomerBffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _factory = new CustomerBffFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
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
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Login_ChallengesCustomerPortalWithPkce()
    {
        var response = await _bff.GetAsync("/bff/login?returnUrl=/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        query["client_id"].ShouldBe(Services.CustomerPortal);
        query["code_challenge"].ShouldNotBeNullOrWhiteSpace();
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"]!.EndsWith("/signin-oidc", StringComparison.Ordinal).ShouldBeTrue();

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe("frame-ancestors 'none'");
        response.Content.ReadAsStringAsync().Result.ShouldNotContain("access_token");
    }

    [Test]
    public async Task BadReturnUrl_Is400()
    {
        var response = await _bff.GetAsync("/bff/login?returnUrl=" + Uri.EscapeDataString("https://evil.example"));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public void SessionCookie_IsHostCookie()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        options.Cookie.Name.ShouldBe(SessionCookie);
        options.Cookie.HttpOnly.ShouldBeTrue();
        options.Cookie.Path.ShouldBe("/");
        options.Cookie.Domain.ShouldBeNull();
        options.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
        options.Cookie.MaxAge.ShouldBeNull();
        options.SessionStore.ShouldBeNull();
    }

    [Test]
    public async Task ApiWithoutCookie_Is401NotRedirect()
    {
        var response = await _bff.GetAsync("/api/users/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("access_token");
    }

    [Test]
    public async Task BlockedPrefixes_Are404()
    {
        (await _bff.GetAsync("/api/instruments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _bff.GetAsync("/api/tenants")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _bff.GetAsync("/api/reports/schedules")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _bff.GetAsync("/api/currencies")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MutatingApiWithoutHeader_Is400()
    {
        var response = await _bff.PutAsync("/api/users/me", JsonContent(new { name = "Cee" }));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLogout_Is405()
    {
        (await _bff.GetAsync("/bff/logout")).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task Customer_SignsIn_AndReadsUsersMe_StaffGetsNoCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Customer Bff", "firm-customer-bff");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-customer-bff", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-customer-bff", "Password1!", tenant.Id, adviser.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-customer-bff", "Password1!", tenant.Id);

        var staff = await SubmitAsync((await _bff.GetAsync("/bff/login")).Headers.Location!, "tess@firm-customer-bff", "Password1!", "firm-customer-bff");
        staff.Code.ShouldBeNull();

        var login = await _bff.GetAsync("/bff/login?returnUrl=/dashboard");
        var result = await SubmitAsync(login.Headers.Location!, "cee@firm-customer-bff", "Password1!", "firm-customer-bff");
        result.Code.ShouldNotBeNullOrWhiteSpace();

        using var callback = new HttpRequestMessage(HttpMethod.Get, result.Callback!.PathAndQuery);
        callback.Headers.TryAddWithoutValidation("Cookie", CookieHeader(login));
        var signedIn = await _bff.SendAsync(callback);
        var session = CookieHeader(signedIn);
        session.ShouldContain(SessionCookie);
        (await signedIn.Content.ReadAsStringAsync()).ShouldNotContain("access_token");

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        me.Headers.TryAddWithoutValidation("Cookie", session);
        var response = await _bff.SendAsync(me);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(customer.PublicId);
        json.RootElement.GetProperty("role").GetString().ShouldBe("customer");

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logout.Headers.TryAddWithoutValidation("Cookie", session);
        logout.Headers.TryAddWithoutValidation("X-MyWealth-Request", "1");
        var loggedOut = await _bff.SendAsync(logout);
        loggedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = loggedOut.Headers.Location!.ToString();
        location.ShouldContain("/connect/logout");
        location.ShouldContain("client_id=customer-portal");
    }

    private async Task RegisterOriginAsync()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(Services.CustomerPortal)
            ?? throw new InvalidOperationException("customer-portal is not registered.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = CustomerPortalTestSecret.Value;
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

    private async Task<LoginStop> SubmitAsync(Uri authorize, string email, string password, string? tenantCode)
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
                    return new LoginStop(earlyTarget, HttpUtility.ParseQueryString(earlyTarget.Query)["code"]);
                }
            }

            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return new LoginStop(null, null);
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
            ["Email"] = email,
            ["Password"] = password,
            ["TenantCode"] = tenantCode ?? "",
            ["__RequestVerificationToken"] = token
        });
        var posted = await _identity.PostAsync(url, form);
        for (var i = 0; i < 8; i++)
        {
            if (posted.Headers.Location is not { } next)
            {
                return new LoginStop(null, null);
            }

            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new LoginStop(target, HttpUtility.ParseQueryString(target.Query)["code"]);
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        return new LoginStop(null, null);
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return "";
        }

        return string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private sealed record LoginStop(Uri? Callback, string? Code);
}
