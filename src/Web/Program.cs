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

// demo-tenant-seed R1–R3: an operator command, never part of a normal start.
var seedDemo = DemoTenantSeed.IsRequested(args);

var builder = WebApplication.CreateBuilder(seedDemo ? args[1..] : args);

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

    // Seed mode runs no Development seed and no SystemAdmin bootstrap, and never listens (R1, R2).
    if (seedDemo)
    {
        try
        {
            await DemoTenantSeed.RunAsync(scope.ServiceProvider, CancellationToken.None);
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            app.Logger.LogError(exception, "Demo seed failed. Nothing was written.");
            Environment.ExitCode = 1;
        }

        return;
    }

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
