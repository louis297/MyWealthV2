using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MyWealthV2.Shared;

namespace MyWealthV2.BffAdviserPortal;

public static class BffLogout
{
    public static WebApplication MapBffLogout(this WebApplication app)
    {
        app.MapPost("/bff/logout", (Delegate)LogoutAsync);
        app.MapGet("/bff/logout", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
        app.MapGet("/bff/logout/continue", (HttpContext http) => Results.Redirect(EndSessionUrl(http)));
        return app;
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        if (!string.Equals(http.Request.Headers[ApiProxy.RequestHeaderName], "1", StringComparison.Ordinal))
        {
            return Results.BadRequest();
        }

        var refreshToken = await http.GetTokenAsync("refresh_token");
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await RevokeAsync(http, refreshToken);
        }

        return Results.Redirect(EndSessionUrl(http));
    }

    private static async Task RevokeAsync(HttpContext http, string refreshToken)
    {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var oidc = http.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var authority = Authority(http);
        using var message = new HttpRequestMessage(HttpMethod.Post, authority + "/connect/revocation")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = Services.AdviserPortal,
                ["client_secret"] = configuration["Authentication:ClientSecret"]
            })
        };
        using var response = await oidc.Backchannel.SendAsync(message, http.RequestAborted);
    }

    private static string EndSessionUrl(HttpContext http)
    {
        var origin = $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}".TrimEnd('/');
        return QueryHelpers.AddQueryString(Authority(http) + "/connect/logout", new Dictionary<string, string?>
        {
            ["client_id"] = Services.AdviserPortal,
            ["post_logout_redirect_uri"] = origin + "/"
        });
    }

    private static string Authority(HttpContext http)
    {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var oidc = http.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        return (configuration["Authentication:Authority"] ?? oidc.Authority ?? "").TrimEnd('/');
    }
}
