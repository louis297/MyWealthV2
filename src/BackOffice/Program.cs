using MudBlazor.Services;
using MyWealthV2.BackOffice;
using MyWealthV2.BackOffice.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-back-office");
builder.AddBackOfficeAuthentication();
builder.AddPlatformApi();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

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
