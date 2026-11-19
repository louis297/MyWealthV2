if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-simulator");

var app = builder.Build();

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);

// Stubs until the control and party surfaces land.
app.MapGet("/control/world", () => Results.Ok());
app.MapGet("/party/status", () => Results.Ok());
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
