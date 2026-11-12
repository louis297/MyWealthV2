using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Schema;
using MyWealthV2.Infrastructure.Identity;
using Scalar.AspNetCore;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var applicator = scope.ServiceProvider.GetRequiredService<SchemaApplicator>();
    await applicator.ApplyAsync(CancellationToken.None);

    var catalog = scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>();
    await catalog.ReloadAsync(CancellationToken.None);

    if (app.Environment.IsDevelopment())
    {
        await DevelopmentIdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        await TestSeed.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        await DevelopmentSmokeSeeder.SeedAsync(
            scope.ServiceProvider,
            app.Configuration[DevelopmentSmokeSeeder.AdviserPasswordKey],
            app.Configuration[DevelopmentSmokeSeeder.CustomerPasswordKey],
            CancellationToken.None);
    }

    // Every environment. After the Development seed, so Development always finds its SystemAdmin
    // (identity-auth amendment 2026-11-12). Throws on bad configuration before the host listens.
    await SystemAdminBootstrap.RunAsync(scope.ServiceProvider, app.Configuration, CancellationToken.None);
}

// No HTTPS redirection or HSTS here: the reverse proxy owns both (production-hosting R11).
app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler(options => { });

app.UseAuthentication();
app.UseAuthorization();

app.Map("/", () => Results.Redirect("/scalar"));

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);


app.Run();
