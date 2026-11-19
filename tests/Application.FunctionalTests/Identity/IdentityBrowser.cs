using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyWealthV2.Application.FunctionalTests.Identity;

/// <summary>
/// One cookie-keeping client on the identity host, for tests that drive authorize, hosted login and logout
/// for several clients in one browser.
/// </summary>
internal sealed class IdentityBrowser : IDisposable
{
    private const string BackOffice = "back-office";
    private const string CustomerPortal = "customer-portal";
    private const string AdviserRedirect = "https://localhost/signin-oidc";
    private const string BackOfficeRedirect = "https://localhost/back-office/signin-oidc";
    private const string Simulator = "simulator";

    /// <summary>The simulator client's redirect URI in the functional identity host.</summary>
    public const string SimulatorRedirect = SimulatorTestSecret.Origin + "/signin-oidc";

    private readonly HttpClient _client = FunctionalTestSetup.Identity.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    public async Task<AuthorizeHit> LoginAsync(string clientId, string email, string password, string? tenantCode)
    {
        var started = await AuthorizeAsync(clientId);
        started.IsLogin.ShouldBeTrue($"Login for {clientId} was already complete: {started.Location}");
        var code = await SubmitLoginAsync(started.Location, clientId, email, password, tenantCode);
        code.ShouldNotBeNullOrWhiteSpace();
        return started with { Code = code };
    }

    public async Task<AuthorizeHit> AuthorizeAsync(string clientId)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var first = await _client.GetAsync(AuthorizeUrl(clientId, challenge));
        var setCookies = first.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join('\n', values)
            : "";
        var response = await FollowAsync(_client, first, RedirectFor(clientId));
        return new AuthorizeHit(response.Location, response.Code, verifier, setCookies);
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
        LastLoginBody = completed.Body;
        return completed.Code;
    }

    /// <summary>The page body of the last <see cref="SubmitLoginAsync"/> that did not end at the client.</summary>
    public string LastLoginBody { get; private set; } = "";

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
        var accessToken = await AccessTokenForAsync(clientId, hit);
        var payload = accessToken.Split('.')[1];
        var jsonPayload = Encoding.UTF8.GetString(Base64UrlDecode(payload));
        using var claims = JsonDocument.Parse(jsonPayload);
        return claims.RootElement.GetProperty("email").GetString()!;
    }

    public async Task<string> AccessTokenForAsync(string clientId, AuthorizeHit hit)
    {
        using var document = JsonDocument.Parse(await TokenResponseForAsync(clientId, hit));
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>Redeems the code of <paramref name="hit"/> and returns the token endpoint's JSON.</summary>
    public async Task<string> TokenResponseForAsync(string clientId, AuthorizeHit hit)
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
        return json;
    }

    /// <summary>The decoded payload of a JWT.</summary>
    public static JsonDocument Payload(string jwt) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(jwt.Split('.')[1])));

    public void Dispose() => _client.Dispose();

    private static string AuthorizeUrl(string clientId, string challenge) =>
        "/connect/authorize?" + string.Join("&",
        [
            "client_id=" + Uri.EscapeDataString(clientId),
            "redirect_uri=" + Uri.EscapeDataString(RedirectFor(clientId)),
            "response_type=code",
            "scope=" + Uri.EscapeDataString(ScopeFor(clientId)),
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

    private static string RedirectFor(string clientId) => clientId switch
    {
        BackOffice => BackOfficeRedirect,
        Simulator => SimulatorRedirect,
        _ => AdviserRedirect
    };

    private static string SecretFor(string clientId) => clientId switch
    {
        BackOffice => BackOfficeTestSecret.Value,
        CustomerPortal => CustomerPortalTestSecret.Value,
        Simulator => SimulatorTestSecret.Value,
        _ => AdviserPortalTestSecret.Value
    };

    // Simulator spec R6: the simulator client asks for no offline_access and no api.
    private static string ScopeFor(string clientId) =>
        clientId == Simulator ? "openid profile email" : "openid profile email offline_access api";

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

internal sealed record AuthorizeHit(string Location, string? Code, string Verifier, string SetCookies = "")
{
    public bool IsLogin =>
        Location.StartsWith("/login", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(Code);
}
