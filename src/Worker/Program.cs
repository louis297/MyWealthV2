using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Worker.Reports;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();
builder.Services.AddScoped<IUser, SystemUser>();
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["reports"];
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

app.MapDefaultEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var catalog = scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>();
    await catalog.ReloadAsync(CancellationToken.None);
}

app.Run();
