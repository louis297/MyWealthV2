using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MyWealthV2.BffAdviserPortal;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBffAuthentication();

var app = builder.Build();

app.UseBffSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapBffApi();
app.MapBffLogout();

var spaOrigin = builder.Configuration["Spa:DevServerUrl"];
if (!string.IsNullOrWhiteSpace(spaOrigin))
{
    var spaClient = new HttpClient { BaseAddress = new Uri(spaOrigin) };
    app.MapFallback(async (HttpContext http) =>
    {
        var path = http.Request.Path;
        if (path.StartsWithSegments("/api")
            || path.StartsWithSegments("/bff")
            || path.StartsWithSegments("/signin-oidc")
            || path.StartsWithSegments("/signout-callback-oidc")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/alive"))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var outbound = new HttpRequestMessage(
            new HttpMethod(http.Request.Method),
            new Uri(spaClient.BaseAddress!, path + http.Request.QueryString));
        using var inbound = await spaClient.SendAsync(outbound, http.RequestAborted);
        http.Response.StatusCode = (int)inbound.StatusCode;
        if (inbound.Content.Headers.ContentType is { } contentType)
        {
            http.Response.ContentType = contentType.ToString();
        }

        await inbound.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
    });
}

app.MapGet("/bff/login", (string? returnUrl) =>
{
    if (!ReturnUrls.IsAllowed(returnUrl))
    {
        return Results.BadRequest();
    }

    var properties = new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
    };
    return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
