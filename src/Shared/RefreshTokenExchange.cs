using System.Collections.Concurrent;
using System.Text.Json;

namespace MyWealthV2.Shared;

public sealed record RefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Refresh single-flight: at most one refresh grant per refresh token is in flight in this process,
/// and a rotated refresh token is answered from memory instead of being presented again.
/// </summary>
public sealed class RefreshTokenExchange
{
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private readonly HttpClient _tokens;
    private readonly Func<string> _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Remembered> _current = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshedTokens?>>> _calls = new(StringComparer.Ordinal);

    public RefreshTokenExchange(
        HttpClient tokens,
        Func<string> tokenEndpoint,
        string clientId,
        string clientSecret,
        TimeProvider time)
    {
        _tokens = tokens;
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _time = time;
    }

    public Task<RefreshedTokens?> ExchangeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var presented = LatestRefreshToken(refreshToken);
        if (TryRecall(presented, out var cached) && cached.ExpiresAt > _time.GetUtcNow().AddMinutes(1))
        {
            return Task.FromResult<RefreshedTokens?>(cached);
        }

        if (_calls.TryGetValue(presented, out var completed)
            && completed.IsValueCreated
            && completed.Value.IsCompleted)
        {
            _calls.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshedTokens?>>>(presented, completed));
        }

        var call = _calls.GetOrAdd(
            presented,
            static (token, self) => new Lazy<Task<RefreshedTokens?>>(() => self.PostAndRememberAsync(token)),
            this);
        return call.Value.WaitAsync(cancellationToken);
    }

    private string LatestRefreshToken(string refreshToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = refreshToken;
        while (seen.Add(current) && TryRecall(current, out var cached) && cached.RefreshToken != current)
        {
            current = cached.RefreshToken;
        }

        return current;
    }

    private bool TryRecall(string refreshToken, out RefreshedTokens result)
    {
        result = null!;
        if (!_current.TryGetValue(refreshToken, out var remembered))
        {
            return false;
        }

        if (remembered.WrittenAt + Retention <= _time.GetUtcNow())
        {
            _current.TryRemove(new KeyValuePair<string, Remembered>(refreshToken, remembered));
            return false;
        }

        result = remembered.Result;
        return true;
    }

    private void Remember(string refreshToken, RefreshedTokens result)
    {
        var now = _time.GetUtcNow();
        foreach (var entry in _current)
        {
            if (entry.Value.WrittenAt + Retention <= now)
            {
                _current.TryRemove(entry);
            }
        }

        _current[refreshToken] = new Remembered(result, now);
        _current[result.RefreshToken] = new Remembered(result, now);
    }

    private async Task<RefreshedTokens?> PostAndRememberAsync(string refreshToken)
    {
        if (TryRecall(refreshToken, out var cached) && cached.ExpiresAt > _time.GetUtcNow().AddMinutes(1))
        {
            return cached;
        }

        var result = await PostAsync(refreshToken, CancellationToken.None);
        if (result is null)
        {
            _calls.TryRemove(refreshToken, out _);
            return null;
        }

        Remember(refreshToken, result);
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
                ["client_id"] = _clientId,
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

    private sealed record Remembered(RefreshedTokens Result, DateTimeOffset WrittenAt);
}
