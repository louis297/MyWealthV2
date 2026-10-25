using Microsoft.AspNetCore.HttpOverrides;
using MyWealthV2.BackOffice;
using MyWealthV2.BackOffice.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddBackOfficeAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto
    };
    forwardedHeaders.KnownIPNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapBackOfficeSignOut();
app.MapGet("/forbidden", () => Results.Content("<h1>Forbidden</h1>", "text/html"))
    .RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(BackOfficeAuthenticationExtensions.SystemAdminPolicy);
app.MapFallback(() => Results.NotFound())
    .RequireAuthorization(BackOfficeAuthenticationExtensions.SystemAdminPolicy);
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
