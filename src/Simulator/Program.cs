using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyWealthV2.Schema;
using MyWealthV2.Shared;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Hosting;
using MyWealthV2.Simulator.Party;
using MyWealthV2.Simulator.Worlds;
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

builder.Services.AddDbContext<SimulatorDbContext>(options => options.UseSqlServer(connectionString));
builder.EnrichSqlServerDbContext<SimulatorDbContext>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new UtcInstantJsonConverter()));

// Simulator spec R31: checked before the host listens, from the configuration, in every environment.
var partyApiKey = PartyEndpoints.ReadPartyApiKey(builder.Configuration);

var app = builder.Build();

// Simulator spec R15: apply database/simulator/ scripts before the host listens.
await SchemaApplicator.ForDatabase(
        connectionString,
        Path.Combine(AppContext.BaseDirectory, "simulator-schema"),
        app.Services.GetRequiredService<ILogger<SchemaApplicator>>())
    .ApplyAsync(CancellationToken.None);
await WorldStartup.EnsureWorldAsync(app.Services, CancellationToken.None);

app.UsePublicHostForwardedHeaders(trustAnyProxyInDevelopment: true);
app.UseSurfacePorts();
app.UseAuthentication();
app.UseAuthorization();

app.MapSimulatorSession();
app.MapOpenApi().RequireAuthorization(SimulatorAuthentication.OperatorPolicy);
app.MapScalarApiReference().RequireAuthorization(SimulatorAuthentication.OperatorPolicy);

app.MapControlEndpoints();
app.MapPartyEndpoints(partyApiKey);
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
