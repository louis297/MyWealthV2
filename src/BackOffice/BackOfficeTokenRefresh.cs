using Microsoft.AspNetCore.Authentication.Cookies;

namespace MyWealthV2.BackOffice;

public sealed record RefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed class BackOfficeTokenRefresh
{
    private readonly HttpClient _tokens;
    private readonly Func<string> _tokenEndpoint;
    private readonly string _clientSecret;
    private readonly TimeProvider _time;

    public BackOfficeTokenRefresh(HttpClient tokens, Func<string> tokenEndpoint, string clientSecret, TimeProvider time)
    {
        _tokens = tokens;
        _tokenEndpoint = tokenEndpoint;
        _clientSecret = clientSecret;
        _time = time;
    }

    public Task<RefreshedTokens?> ExchangeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        _ = refreshToken;
        _ = cancellationToken;
        _ = _tokens;
        _ = _tokenEndpoint();
        _ = _clientSecret;
        _ = _time.GetUtcNow();
        return Task.FromResult<RefreshedTokens?>(null);
    }

    public Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        context.ShouldRenew = true;
        return Task.CompletedTask;
    }
}
