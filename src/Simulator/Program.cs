using MyWealthV2.Schema;
using MyWealthV2.Shared;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Hosting;
using Scalar.AspNetCore;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPublicHostForwardedHeaders();
builder.AddPublicHostDataProtection("mywealth-simulator");
builder.AddSimulatorAuthentication();
builder.Services.AddOpenApi();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapSimulatorSession();
app.MapOpenApi().RequireAuthorization(SimulatorAuthentication.OperatorPolicy);
app.MapScalarApiReference().RequireAuthorization(SimulatorAuthentication.OperatorPolicy);

// Stubs until the world and the party surface land.
app.MapGet("/control/world", () => Results.Ok()).RequireAuthorization(SimulatorAuthentication.OperatorPolicy);
app.MapGet("/party/status", () => Results.Ok());
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
