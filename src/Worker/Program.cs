using MyWealthV2.Infrastructure.CorporateActions;
using Hangfire;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Fx;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.BankReconciliations;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Schema;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.Interest;
using MyWealthV2.Infrastructure.MarketData;

if (HealthCheckCommand.IsRequested(args))
{
    Environment.ExitCode = await HealthCheckCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructureServices();
builder.AddBankConnections(BankConnectionHost.Worker);
builder.AddBrokerConnections(BrokerConnectionHost.Worker);
builder.AddCustodianConnections(MyWealthV2.Infrastructure.Custodian.CustodianConnectionHost.Worker);
builder.AddMarketDataConnections(MarketDataHost.Worker);
builder.AddMarketDataHistory();
builder.Services.AddScoped<IUser, SystemUser>();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddScoped<FxRateSyncJob>();
builder.Services.AddScoped(sp => new InstrumentPriceSyncJob(
    sp.GetRequiredService<IActiveInstrumentCatalog>(),
    sp.GetRequiredService<IInstrumentPriceSource>(),
    sp.GetRequiredService<IInstrumentPriceStore>(),
    sp.GetRequiredService<MarketDataConnections>(),
    sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromSeconds(8),
    sp.GetRequiredService<ILogger<InstrumentPriceSyncJob>>()));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["reports", "rebalance", "outbox", "fx", "prices", BankStatementRecurringJob.Queue, InterestRunJob.Queue, MarketDataRecurringJob.Queue, BrokerSyncRecurringJob.Queue, MyWealthV2.Infrastructure.Custodian.CustodianSyncRecurringJob.Queue];
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var applicator = scope.ServiceProvider.GetRequiredService<SchemaApplicator>();
    await applicator.ApplyAsync(CancellationToken.None);

    var catalog = scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>();
    await catalog.ReloadAsync(CancellationToken.None);
}

OutboxRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
FxRateRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
BankReconciliationRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
MyWealthV2.Infrastructure.Custodian.CustodianReconciliationRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
MyWealthV2.Infrastructure.Custodian.CustodianSyncRecurringJob.Register(
    app.Services.GetRequiredService<IRecurringJobManager>(),
    app.Services.GetRequiredService<JobStorage>(),
    app.Services.GetRequiredService<MyWealthV2.Infrastructure.Custodian.ConfiguredCustodianConnections>());
InterestRunRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
InstrumentPriceRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
CorporateActionApplyRecurringJob.Register(app.Services.GetRequiredService<IRecurringJobManager>());
BankStatementRecurringJob.Register(
    app.Services.GetRequiredService<IRecurringJobManager>(),
    app.Services.GetRequiredService<JobStorage>(),
    app.Services.GetRequiredService<ConfiguredBankConnections>());
BrokerSyncRecurringJob.Register(
    app.Services.GetRequiredService<IRecurringJobManager>(),
    app.Services.GetRequiredService<JobStorage>(),
    app.Services.GetRequiredService<ConfiguredBrokerConnections>());
MarketDataRecurringJob.Register(
    app.Services.GetRequiredService<IRecurringJobManager>(),
    app.Services.GetRequiredService<JobStorage>(),
    app.Services.GetRequiredService<ConfiguredMarketDataConnections>());

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
