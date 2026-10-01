extern alias BffHost;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffApiTests
{
    private const string RequestHeader = "X-MyWealth-Request";

    private RecordingWebApi _api = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;
    private string _sessionCookie = "";

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _api = new RecordingWebApi();
        _factory = new BffFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
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
        await RegisterBffOriginAsync();
        _sessionCookie = await SignInAsync();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        _identity.Dispose();
        await _factory.DisposeAsync();
    }

    [SetUp]
    public void ClearCalls() => _api.Calls.Clear();

    [Test]
    public async Task NoCookie_OnUsersMe_Is401NotRedirect()
    {
        var response = await _bff.GetAsync("/api/users/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Unauthorized");
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
        _api.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Session_ProxiesUsersMe_WithBearerAndNoBrowserCookie()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer browser-token");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var call = _api.Calls.Single();
        call.Path.ShouldBe("/users/me");
        call.HasCookie.ShouldBeFalse();
        call.Authorization.ShouldNotBeNull();
        call.Authorization.ShouldStartWith("Bearer ");
        call.Authorization.ShouldNotContain("browser-token");
        call.Accept.ShouldContain("application/json");
    }

    [Test]
    public async Task MutatingCall_WithoutRequestHeader_Is400AndNotForwarded()
    {
        var before = _api.Calls.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/customers");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _api.Calls.Count.ShouldBe(before);
    }

    [Test]
    public async Task MutatingCall_ForwardsIdempotencyKeyAndContentType()
    {
        _api.Scripts["/users/customers"] = (HttpStatusCode.Created, """{"id":"cust"}""");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/customers");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        request.Headers.TryAddWithoutValidation(RequestHeader, "1");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "key-1");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"id":"cust"}""");
        var call = _api.Calls.Single(item => item.Path == "/users/customers");
        call.IdempotencyKey.ShouldBe("key-1");
        call.ContentType.ShouldNotBeNull();
        call.ContentType.ShouldContain("application/json");
        call.HasCookie.ShouldBeFalse();
    }

    [Test]
    public async Task Get_DoesNotRequireRequestHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/currencies");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        _api.Scripts["/currencies"] = (HttpStatusCode.OK, "[]");

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Calls.ShouldContain(call => call.Path == "/currencies");
    }

    [TestCase("/api/health")]
    [TestCase("/api/scalar")]
    public async Task UnknownApiPrefix_Is404AndNotForwarded(string path)
    {
        var before = _api.Calls.Count;
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("title").GetString().ShouldBe("Not Found");
        _api.Calls.Count.ShouldBe(before);
    }

    [Test]
    public async Task ProxiedErrorBody_IsCopied()
    {
        _api.Scripts["/users/missing"] = (HttpStatusCode.NotFound, """{"title":"missing-row"}""");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/missing");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);

        var response = await _bff.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"title":"missing-row"}""");
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

    private async Task<string> SignInAsync()
    {
        var login = await _bff.GetAsync("/bff/login?returnUrl=/customers");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cookie = CookieHeader(login);
        var callback = await SignInUntilCallbackAsync(login.Headers.Location!);
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callback.PathAndQuery);
        callbackRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
        var signedIn = await _bff.SendAsync(callbackRequest);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return CookieHeader(signedIn);
    }

    private async Task<Uri> SignInUntilCallbackAsync(Uri authorize)
    {
        var url = authorize.IsAbsoluteUri ? authorize.PathAndQuery : authorize.ToString();
        for (var i = 0; i < 8; i++)
        {
            var response = await _identity.GetAsync(url);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Authorize did not reach hosted login. {(int)response.StatusCode} {body}");
            }

            var next = response.Headers.Location ?? throw new InvalidOperationException("Authorize redirect had no Location.");
            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var loginHtml = await (await _identity.GetAsync(url)).Content.ReadAsStringAsync();
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
        var posted = await _identity.PostAsync(url, form);
        var callbackPrefix = _bff.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            if (posted.Headers.Location is not { } next)
            {
                var body = await posted.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Login did not return a code. {(int)posted.StatusCode} {body}");
            }

            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(callbackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        throw new InvalidOperationException("Authorize did not return a code.");
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
