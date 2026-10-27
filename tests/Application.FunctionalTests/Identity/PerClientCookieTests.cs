using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class PerClientCookieTests : TestBase
{
    private const string AdviserPortal = "adviser-portal";
    private const string BackOffice = "back-office";
    private const string AdviserRedirect = "https://localhost/signin-oidc";
    private const string AdviserPostLogout = "https://localhost/";
    private const string BackOfficeRedirect = "https://localhost/back-office/signin-oidc";
    private const string AdviserPortalCookie = ".AspNetCore.Identity.AdviserPortal";
    private const string BackOfficeCookie = ".AspNetCore.Identity.BackOffice";

    [Test]
    public async Task SystemAdminOnBackOffice_DoesNotSilentlyAuthorizeAdviserPortal()
    {
        using var browser = new IdentityBrowser();
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.IsLogin.ShouldBeTrue($"Adviser Portal authorize completed silently: {adviser.Location}");
        adviser.Code.ShouldBeNull();

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.Code.ShouldNotBeNullOrWhiteSpace();
        backOffice.IsLogin.ShouldBeFalse();
    }

    [Test]
    public async Task TenantAdminOnAdviserPortal_DoesNotAuthorizeBackOffice_AndKeepsPortalSession()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Cookies", "firm-cookies");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-cookies", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-cookies", "Password1!", "firm-cookies");

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.IsLogin.ShouldBeTrue($"Back Office authorize completed silently: {backOffice.Location}");
        backOffice.Code.ShouldBeNull();

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.Code.ShouldNotBeNullOrWhiteSpace();
        adviser.IsLogin.ShouldBeFalse();
    }

    [Test]
    public async Task BackOfficeLogin_DoesNotReplaceAdviserPortalSubject()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Isolate", "firm-isolate");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-isolate", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-isolate", "Password1!", "firm-isolate");

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.IsLogin.ShouldBeTrue();
        var backOfficeCode = await browser.SubmitLoginAsync(
            backOffice.Location,
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);
        backOfficeCode.ShouldNotBeNullOrWhiteSpace();

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(AdviserPortal, adviser)).ShouldBe("tess@firm-isolate");
    }

    [Test]
    public async Task AdviserPortalLogout_ClearsOnlyThatIdentityCookie()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Logout", "firm-logout");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-logout", "Password1!", tenant.Id);

        using var browser = new IdentityBrowser();
        await browser.LoginAsync(AdviserPortal, "tess@firm-logout", "Password1!", "firm-logout");
        await browser.LoginAsync(
            BackOffice,
            DevelopmentIdentitySeeder.SystemAdminEmail,
            DevelopmentIdentitySeeder.SystemAdminPassword,
            tenantCode: null);

        var logout = await browser.LogoutAsync(AdviserPortal, AdviserPostLogout);
        logout.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        logout.Headers.Location.ShouldNotBeNull();
        logout.Headers.Location.ToString().ShouldBe(AdviserPostLogout);

        var setCookies = logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join('\n', values)
            : "";
        setCookies.ShouldContain(AdviserPortalCookie);
        setCookies.ShouldNotContain(BackOfficeCookie);

        var adviser = await browser.AuthorizeAsync(AdviserPortal);
        adviser.IsLogin.ShouldBeTrue();
        adviser.Code.ShouldBeNull();

        var backOffice = await browser.AuthorizeAsync(BackOffice);
        backOffice.Code.ShouldNotBeNullOrWhiteSpace();
        (await browser.EmailForAsync(BackOffice, backOffice)).ShouldBe(DevelopmentIdentitySeeder.SystemAdminEmail);
    }

    private sealed class IdentityBrowser : IDisposable
    {
        private readonly HttpClient _client = FunctionalTestSetup.Identity.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        public async Task LoginAsync(string clientId, string email, string password, string? tenantCode)
        {
            var started = await AuthorizeAsync(clientId);
            started.IsLogin.ShouldBeTrue($"Login for {clientId} was already complete: {started.Location}");
            var code = await SubmitLoginAsync(started.Location, clientId, email, password, tenantCode);
            code.ShouldNotBeNullOrWhiteSpace();
        }

        public async Task<AuthorizeHit> AuthorizeAsync(string clientId)
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var authorize = AuthorizeUrl(clientId, challenge);
            var response = await FollowAsync(_client, authorize, RedirectFor(clientId));
            return new AuthorizeHit(response.Location, response.Code, verifier);
        }

        public async Task<string?> SubmitLoginAsync(
            string loginUrl,
            string clientId,
            string email,
            string password,
            string? tenantCode)
        {
            var loginPage = await _client.GetAsync(loginUrl);
            var loginHtml = await loginPage.Content.ReadAsStringAsync();
            loginPage.StatusCode.ShouldBe(HttpStatusCode.OK);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["Email"] = email,
                ["Password"] = password,
                ["TenantCode"] = tenantCode ?? "",
                ["__RequestVerificationToken"] = Antiforgery(loginHtml)
            });

            var afterLogin = await _client.PostAsync(loginUrl, form);
            var completed = await FollowAsync(_client, afterLogin, RedirectFor(clientId));
            return completed.Code;
        }

        public async Task<HttpResponseMessage> LogoutAsync(string clientId, string postLogoutRedirectUri)
        {
            var url = "/connect/logout?" + string.Join("&",
            [
                "client_id=" + Uri.EscapeDataString(clientId),
                "post_logout_redirect_uri=" + Uri.EscapeDataString(postLogoutRedirectUri)
            ]);
            var response = await _client.GetAsync(url);
            await response.Content.LoadIntoBufferAsync();
            return response;
        }

        public async Task<string> EmailForAsync(string clientId, AuthorizeHit hit)
        {
            hit.Code.ShouldNotBeNullOrWhiteSpace();
            using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = SecretFor(clientId),
                ["redirect_uri"] = RedirectFor(clientId),
                ["code"] = hit.Code,
                ["code_verifier"] = hit.Verifier
            });

            var tokenResponse = await _client.PostAsync("/connect/token", tokenRequest);
            var json = await tokenResponse.Content.ReadAsStringAsync();
            tokenResponse.IsSuccessStatusCode.ShouldBeTrue(json);

            using var document = JsonDocument.Parse(json);
            var accessToken = document.RootElement.GetProperty("access_token").GetString()!;
            var payload = accessToken.Split('.')[1];
            var jsonPayload = Encoding.UTF8.GetString(Base64UrlDecode(payload));
            using var claims = JsonDocument.Parse(jsonPayload);
            return claims.RootElement.GetProperty("email").GetString()!;
        }

        public void Dispose() => _client.Dispose();

        private static string AuthorizeUrl(string clientId, string challenge) =>
            "/connect/authorize?" + string.Join("&",
            [
                "client_id=" + Uri.EscapeDataString(clientId),
                "redirect_uri=" + Uri.EscapeDataString(RedirectFor(clientId)),
                "response_type=code",
                "scope=" + Uri.EscapeDataString("openid profile email offline_access api"),
                "code_challenge=" + challenge,
                "code_challenge_method=S256",
                "state=" + Base64Url(RandomNumberGenerator.GetBytes(16))
            ]);

        private static async Task<Followed> FollowAsync(HttpClient client, string url, string redirectUri)
        {
            var response = await client.GetAsync(url);
            return await FollowAsync(client, response, redirectUri);
        }

        private static async Task<Followed> FollowAsync(
            HttpClient client,
            HttpResponseMessage response,
            string redirectUri)
        {
            for (var i = 0; i < 8; i++)
            {
                if (response.Headers.Location is not { } location)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return new Followed((int)response.StatusCode, body, Location: response.RequestMessage?.RequestUri?.ToString() ?? "", Code: null);
                }

                var target = location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);
                if (target.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                {
                    return new Followed((int)response.StatusCode, Body: "", target.PathAndQuery, Code: null);
                }

                if (target.AbsoluteUri.StartsWith(redirectUri, StringComparison.OrdinalIgnoreCase))
                {
                    return new Followed(
                        (int)response.StatusCode,
                        Body: "",
                        target.AbsoluteUri,
                        HttpUtility.ParseQueryString(target.Query)["code"]);
                }

                response.Dispose();
                response = await client.GetAsync(target);
            }

            return new Followed(0, "too many redirects", "", Code: null);
        }

        private static string RedirectFor(string clientId) =>
            clientId == BackOffice ? BackOfficeRedirect : AdviserRedirect;

        private static string SecretFor(string clientId) =>
            clientId == BackOffice ? BackOfficeTestSecret.Value : AdviserPortalTestSecret.Value;

        private static string Antiforgery(string html)
        {
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(token))
            {
                token = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
            }

            return token;
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }

        private sealed record Followed(int StatusCode, string Body, string Location, string? Code);
    }

    private sealed record AuthorizeHit(string Location, string? Code, string Verifier)
    {
        public bool IsLogin =>
            Location.StartsWith("/login", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(Code);
    }
}
