using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Fx;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using MyWealthV2.Worker.Reports;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();
builder.Services.AddScoped<IUser, SystemUser>();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddScoped<FxRateSyncJob>();
builder.Services.AddScoped(sp => new InstrumentPriceSyncJob(
    sp.GetRequiredService<IActiveInstrumentCatalog>(),
    sp.GetRequiredService<IInstrumentPriceSource>(),
    sp.GetRequiredService<IInstrumentPriceStore>(),
    sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(8),
    sp.GetRequiredService<ILogger<InstrumentPriceSyncJob>>()));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["reports", "outbox", "fx", "prices"];
});

var app = builder.Build();

OutboxRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
FxRateRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
InstrumentPriceRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());

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
