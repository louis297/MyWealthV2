using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MyWealthV2.Shared;

namespace MyWealthV2.BackOffice;

public static class BackOfficeSignOut
{
    public static WebApplication MapBackOfficeSignOut(this WebApplication app)
    {
        app.MapPost("/sign-out", (Delegate)SignOutAsync).RequireAuthorization();
        app.MapGet("/sign-out", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .AllowAnonymous();
        return app;
    }

    private static async Task<IResult> SignOutAsync(HttpContext http)
    {
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(http))
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
        using var message = new HttpRequestMessage(HttpMethod.Post, Authority(http) + "/connect/revocation")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = Services.BackOffice,
                ["client_secret"] = configuration["Authentication:ClientSecret"]
            })
        };
        using var response = await oidc.Backchannel.SendAsync(message, http.RequestAborted);
    }

    private static string EndSessionUrl(HttpContext http)
    {
        return QueryHelpers.AddQueryString(Authority(http) + "/connect/logout", new Dictionary<string, string?>
        {
            ["client_id"] = Services.BackOffice,
            ["post_logout_redirect_uri"] = Origin(http) + "/"
        });
    }

    private static string Origin(HttpContext http)
    {
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()["Authentication:PublicOrigin"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        return $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}".TrimEnd('/');
    }

    private static string Authority(HttpContext http)
    {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        var oidc = http.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        return (configuration["Authentication:Authority"] ?? oidc.Authority ?? "").TrimEnd('/');
    }
}
