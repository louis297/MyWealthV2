using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

public class BackOfficeSignOutTests
{
    private const string SessionCookieName = "__Host-back-office";
    private const string RefreshToken = "refresh-1";

    private RevocationCapture _capture = null!;
    private BackOfficeFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Start()
    {
        using var identity = FunctionalTestSetup.Identity.CreateClient();
        _capture = new RevocationCapture(FunctionalTestSetup.Identity.Server.CreateHandler());
        _factory = new BackOfficeFactory(identity.BaseAddress!.ToString(), _capture);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _client.Dispose();
        _capture.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task PostSignOut_RevokesRefresh_AndEndsTheSession()
    {
        using var homeRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        homeRequest.Headers.TryAddWithoutValidation("Cookie", SessionCookie());
        var home = await _client.SendAsync(homeRequest);
        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await home.Content.ReadAsStringAsync();
        html.ShouldContain("action=\"/sign-out\"");

        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        token.ShouldNotBeNullOrWhiteSpace();
        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/sign-out")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            })
        };
        signOut.Headers.TryAddWithoutValidation("Cookie", Merge(SessionCookie(), CookieHeader(home)));
        var response = await _client.SendAsync(signOut);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        location!.AbsolutePath.ShouldContain("/connect/logout");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe(Services.BackOffice);
        query["post_logout_redirect_uri"].ShouldNotBeNull();
        query["post_logout_redirect_uri"]!.EndsWith("/", StringComparison.Ordinal).ShouldBeTrue();
        _capture.Body.ShouldNotBeNull();
        var revocation = HttpUtility.ParseQueryString(_capture.Body);
        revocation["token"].ShouldBe(RefreshToken);
        revocation["token_type_hint"].ShouldBe("refresh_token");
        revocation["client_id"].ShouldBe(Services.BackOffice);
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie =>
            cookie.StartsWith(SessionCookieName + "=", StringComparison.Ordinal)
            && cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        var again = await _client.GetAsync("/");
        again.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        again.Headers.Location!.AbsolutePath.ShouldContain("/connect/authorize");
    }

    [Test]
    public async Task GetSignOut_IsNotAllowed()
    {
        var response = await _client.GetAsync("/sign-out");

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    private string SessionCookie()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(
            [new Claim("role", "systemAdmin"), new Claim("name", "Test")],
            CookieAuthenticationDefaults.AuthenticationScheme,
            "name",
            "role");
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "access-1" },
            new AuthenticationToken { Name = "refresh_token", Value = RefreshToken }
        ]);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);
        return SessionCookieName + "=" + options.TicketDataFormat.Protect(ticket);
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return "";
        }

        return string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
    }

    private static string Merge(string left, string right)
    {
        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return left + "; " + right;
    }

    private sealed class RevocationCapture(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/connect/revocation", StringComparison.OrdinalIgnoreCase) == true
                && request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                request.Content = new StringContent(Body, Encoding.UTF8, "application/x-www-form-urlencoded");
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
