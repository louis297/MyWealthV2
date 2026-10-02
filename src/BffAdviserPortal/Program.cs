using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using MyWealthV2.BffAdviserPortal;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBffAuthentication();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // The Aspire dev proxy is not always a loopback address.
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);
}

app.UseBffSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapBffApi();
app.MapBffLogout();

var spaOrigin = builder.Configuration["Spa:DevServerUrl"];
if (!string.IsNullOrWhiteSpace(spaOrigin))
{
    DevelopmentSpaProxy.Map(app, spaOrigin);
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
