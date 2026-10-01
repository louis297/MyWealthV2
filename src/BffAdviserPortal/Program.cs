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
