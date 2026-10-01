extern alias BffHost;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffAllowListTests : TestBase
{
    private const string SessionCookie = "__Host-bff-adviser-portal";

    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BffFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            api: new HandlerWebApi(FunctionalTestSetup.WebApiHandler),
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
    public async Task Customer_CorrectPassword_GetsNoCodeAndNoBffCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Bff", "firmbff");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firmbff", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firmbff", "Password1!", tenant.Id, adviser.Id);

        var login = await _bff.GetAsync("/bff/login");
        var result = await SubmitAsync(login.Headers.Location!, "cee@firmbff", "Password1!", "firmbff");

        result.Code.ShouldBeNull();
        result.SetCookie.ShouldNotContain(value => value.StartsWith(SessionCookie, StringComparison.Ordinal));
    }

    [Test]
    public async Task SystemAdmin_GetsASession_AndUsersMe()
    {
        var login = await _bff.GetAsync("/bff/login");
        var result = await SubmitAsync(
            login.Headers.Location!,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        result.Code.ShouldNotBeNullOrWhiteSpace();

        using var callback = new HttpRequestMessage(HttpMethod.Get, result.Callback!.PathAndQuery);
        callback.Headers.TryAddWithoutValidation("Cookie", CookieHeader(login));
        var signedIn = await _bff.SendAsync(callback);
        var session = CookieHeader(signedIn);
        session.ShouldContain(SessionCookie);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        me.Headers.TryAddWithoutValidation("Cookie", session);
        var response = await _bff.SendAsync(me);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("role").GetString().ShouldBe("systemAdmin");
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
                    return new LoginStop(earlyTarget, HttpUtility.ParseQueryString(earlyTarget.Query)["code"], []);
                }
            }

            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return new LoginStop(null, null, SetCookies(response));
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
                return new LoginStop(null, null, SetCookies(posted));
            }

            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new LoginStop(target, HttpUtility.ParseQueryString(target.Query)["code"], SetCookies(posted));
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        return new LoginStop(null, null, []);
    }

    private static string CookieHeader(HttpResponseMessage response) =>
        string.Join("; ", SetCookies(response).Select(value => value.Split(';', 2)[0]));

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];

    private sealed record LoginStop(Uri? Callback, string? Code, IReadOnlyList<string> SetCookie);

    private sealed class HandlerWebApi(HttpMessageHandler inner) : BffHost::MyWealthV2.BffAdviserPortal.IWebApiTransport
    {
        private readonly HttpMessageInvoker _invoker = new(inner, disposeHandler: false);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _invoker.SendAsync(request, cancellationToken);
    }
}
