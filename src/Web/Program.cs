using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data.Schema;
using MyWealthV2.Infrastructure.Identity;
using Scalar.AspNetCore;

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
    }
}

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors(static builder =>
    builder.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());

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
