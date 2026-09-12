using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

public sealed class OpenIdConnectTestClient(Func<HttpClient> createClient)
{
    private const string RedirectUri = "https://localhost/callback";
    private const string ClientId = "adviser-portal";

    public async Task<TokenResponse> SignInAsync(string email, string password, string? tenantCode)
    {
        using var identityClient = CreateClient();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var authorize = "/connect/authorize?" + string.Join("&",
        [
            "client_id=" + Uri.EscapeDataString(ClientId),
            "redirect_uri=" + Uri.EscapeDataString(RedirectUri),
            "response_type=code",
            "scope=" + Uri.EscapeDataString("openid profile offline_access api"),
            "code_challenge=" + challenge,
            "code_challenge_method=S256",
            "state=" + state
        ]);

        var location = await FollowToLoginAsync(identityClient, authorize);
        var loginHtml = await (await identityClient.GetAsync(location)).Content.ReadAsStringAsync();
        var token = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(loginHtml, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var login = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["TenantCode"] = tenantCode ?? "",
            ["__RequestVerificationToken"] = token
        });

        var afterLogin = await identityClient.PostAsync(location, login);
        var code = await FollowForCodeAsync(identityClient, afterLogin);

        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier
        });

        var tokenResponse = await identityClient.PostAsync("/connect/token", tokenRequest);
        var json = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint failed: {(int)tokenResponse.StatusCode} {json}");
        }

        using var document = JsonDocument.Parse(json);
        return new TokenResponse(
            document.RootElement.GetProperty("access_token").GetString()!,
            document.RootElement.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString()
                : null);
    }

    public async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var identityClient = CreateClient();
        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken
        });
        var response = await identityClient.PostAsync("/connect/token", tokenRequest);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    public async Task<bool> LoginFailsAsync(string email, string password, string? tenantCode)
    {
        try
        {
            await SignInAsync(email, password, tenantCode);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private HttpClient CreateClient()
    {
        var identityClient = createClient();
        identityClient.Timeout = TimeSpan.FromSeconds(30);
        return identityClient;
    }

    private static async Task<string> FollowToLoginAsync(HttpClient client, string url)
    {
        for (var i = 0; i < 8; i++)
        {
            var response = await client.GetAsync(url);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return url;
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

    private static async Task<string> FollowForCodeAsync(HttpClient client, HttpResponseMessage response)
    {
        for (var i = 0; i < 8; i++)
        {
            if (response.Headers.Location is { } location)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);
                if (target.AbsoluteUri.StartsWith(RedirectUri, StringComparison.OrdinalIgnoreCase))
                {
                    var code = HttpUtility.ParseQueryString(target.Query)["code"];
                    if (string.IsNullOrEmpty(code))
                    {
                        throw new InvalidOperationException("Callback had no code: " + target);
                    }

                    return code;
                }

                response.Dispose();
                response = await client.GetAsync(location);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Login did not return a code. {(int)response.StatusCode} {body}");
        }

        throw new InvalidOperationException("Authorize did not return a code.");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record TokenResponse(string AccessToken, string? RefreshToken);
