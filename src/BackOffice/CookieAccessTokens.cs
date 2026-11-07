using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MyWealthV2.BackOffice;

public sealed class CookieAccessTokens(IHttpContextAccessor http) : IAccessTokens
{
    private string? _access;
    private string? _refresh;
    private bool _captured;

    public string? AccessToken
    {
        get
        {
            Capture();
            return _access;
        }
    }

    public string? RefreshToken
    {
        get
        {
            Capture();
            return _refresh;
        }
    }

    public void Store(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        _access = accessToken;
        _refresh = refreshToken;
        _captured = true;
        var context = http.HttpContext;
        if (context is null || context.Response.HasStarted)
        {
            return;
        }

        var authenticated = context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .GetAwaiter()
            .GetResult();
        if (authenticated.Principal is null || authenticated.Properties is null)
        {
            return;
        }

        var tokens = authenticated.Properties.GetTokens().ToList();
        Set(tokens, "access_token", accessToken);
        Set(tokens, "refresh_token", refreshToken);
        Set(tokens, "expires_at", expiresAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        authenticated.Properties.StoreTokens(tokens);
        context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                authenticated.Principal,
                authenticated.Properties)
            .GetAwaiter()
            .GetResult();
    }

    private void Capture()
    {
        if (_captured)
        {
            return;
        }

        var context = http.HttpContext;
        if (context is null)
        {
            return;
        }

        _access = context.GetTokenAsync("access_token").GetAwaiter().GetResult();
        _refresh = context.GetTokenAsync("refresh_token").GetAwaiter().GetResult();
        _captured = true;
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
