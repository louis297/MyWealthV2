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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();
app.MapFallback(() => Results.NotFound()).RequireAuthorization();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
