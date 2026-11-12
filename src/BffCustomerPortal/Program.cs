using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MyWealthV2.BffCustomerPortal;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-bff-customer-portal");
builder.AddBffAuthentication();

var app = builder.Build();

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);

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
