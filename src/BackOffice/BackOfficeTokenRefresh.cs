using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public sealed record RefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed class BackOfficeTokenRefresh
{
    private readonly HttpClient _tokens;
    private readonly Func<string> _tokenEndpoint;
    private readonly string _clientSecret;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, RefreshedTokens> _current = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshedTokens?>>> _calls = new(StringComparer.Ordinal);

    public BackOfficeTokenRefresh(HttpClient tokens, Func<string> tokenEndpoint, string clientSecret, TimeProvider time)
    {
        _tokens = tokens;
        _tokenEndpoint = tokenEndpoint;
        _clientSecret = clientSecret;
        _time = time;
    }

    public Task<RefreshedTokens?> ExchangeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (_current.TryGetValue(refreshToken, out var cached) && cached.ExpiresAt > _time.GetUtcNow().AddMinutes(1))
        {
            return Task.FromResult<RefreshedTokens?>(cached);
        }

        var call = _calls.GetOrAdd(
            refreshToken,
            static (presented, self) => new Lazy<Task<RefreshedTokens?>>(() => self.PostAndRememberAsync(presented)),
            this);
        return call.Value.WaitAsync(cancellationToken);
    }

    private async Task<RefreshedTokens?> PostAndRememberAsync(string refreshToken)
    {
        if (_current.TryGetValue(refreshToken, out var cached) && cached.ExpiresAt > _time.GetUtcNow().AddMinutes(1))
        {
            return cached;
        }

        var result = await PostAsync(refreshToken, CancellationToken.None);
        if (result is null)
        {
            _calls.TryRemove(refreshToken, out _);
            return null;
        }

        _current[refreshToken] = result;
        _current[result.RefreshToken] = result;
        return result;
    }

    private async Task<RefreshedTokens?> PostAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint())
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = Services.BackOffice,
                ["client_secret"] = _clientSecret
            })
        };
        using var response = await _tokens.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var accessToken = document.RootElement.GetProperty("access_token").GetString();
        var nextRefresh = document.RootElement.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : refreshToken;
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
            ? seconds
            : 900;
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(nextRefresh))
        {
            return null;
        }

        return new RefreshedTokens(accessToken, nextRefresh, _time.GetUtcNow().AddSeconds(expiresIn));
    }

    public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Properties is null)
        {
            return;
        }

        var refreshToken = context.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var expiresText = context.Properties.GetTokenValue("expires_at");
        if (DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            && expiresAt > _time.GetUtcNow().AddMinutes(1))
        {
            return;
        }

        var next = await ExchangeAsync(refreshToken, context.HttpContext.RequestAborted);
        if (next is null)
        {
            return;
        }

        var tokens = context.Properties.GetTokens().ToList();
        Set(tokens, "access_token", next.AccessToken);
        Set(tokens, "refresh_token", next.RefreshToken);
        Set(tokens, "expires_at", next.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
        context.Properties.StoreTokens(tokens);
        context.ShouldRenew = true;
    }

    private static void Set(List<AuthenticationToken> tokens, string name, string value)
    {
        var index = tokens.FindIndex(token => token.Name == name);
        if (index >= 0)
        {
            tokens[index] = new AuthenticationToken { Name = name, Value = value };
            return;
        }

        tokens.Add(new AuthenticationToken { Name = name, Value = value });
    }
}
