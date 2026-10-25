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

    public void Store(string accessToken, string refreshToken)
    {
        _access = accessToken;
        _refresh = refreshToken;
        _captured = true;
        var context = http.HttpContext;
        if (context is null)
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

        authenticated.Properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = accessToken },
            new AuthenticationToken { Name = "refresh_token", Value = refreshToken }
        ]);
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
        if (context is not null)
        {
            _access = context.GetTokenAsync("access_token").GetAwaiter().GetResult();
            _refresh = context.GetTokenAsync("refresh_token").GetAwaiter().GetResult();
        }

        _captured = true;
    }
}
