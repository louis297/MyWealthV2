using MyWealthV2.Schema;
using MyWealthV2.Shared;
using MyWealthV2.Simulator.Hosting;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-simulator");

// Simulator spec R14: the simulator's own database, and no other.
var connectionString = builder.Configuration.GetConnectionString(Services.SimulatorDatabase);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException($"Connection string '{Services.SimulatorDatabase}' not found.");
}

var app = builder.Build();

// Simulator spec R15: apply database/simulator/ scripts before the host listens.
await SchemaApplicator.ForDatabase(
        connectionString,
        Path.Combine(AppContext.BaseDirectory, "simulator-schema"),
        app.Services.GetRequiredService<ILogger<SchemaApplicator>>())
    .ApplyAsync(CancellationToken.None);

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);
app.UseSurfacePorts();

// Stubs until the control and party surfaces land.
app.MapGet("/control/world", () => Results.Ok());
app.MapGet("/party/status", () => Results.Ok());
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
