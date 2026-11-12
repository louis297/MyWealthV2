using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MyWealthV2.BffAdviserPortal;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-bff-adviser-portal");
builder.AddBffAuthentication();

var app = builder.Build();
var spaOrigin = builder.Configuration["Spa:DevServerUrl"];
var serveSpaBuild = string.IsNullOrWhiteSpace(spaOrigin);

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);

app.UseBffSecurityHeaders();
if (serveSpaBuild)
{
    app.UseSpaStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapBffApi();
app.MapBffLogout();

if (serveSpaBuild)
{
    app.MapSpaFallback();
}
else
{
    DevelopmentSpaProxy.Map(app, spaOrigin!);
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
