using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public sealed class BackOfficeTokenRefresh
{
    private readonly RefreshTokenExchange _exchange;
    private readonly TimeProvider _time;

    public BackOfficeTokenRefresh(HttpClient tokens, Func<string> tokenEndpoint, string clientSecret, TimeProvider time)
    {
        _exchange = new RefreshTokenExchange(tokens, tokenEndpoint, Services.BackOffice, clientSecret, time);
        _time = time;
    }

    public Task<RefreshedTokens?> ExchangeAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        _exchange.ExchangeAsync(refreshToken, cancellationToken);

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
