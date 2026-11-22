using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Infrastructure.CorporateActions;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Corporate-actions R24: the recurring job <c>corporate-action-apply</c> on a worker host.</summary>
public class CorporateActionApplyJobTests
{
    [Test]
    public void Register_AddsTheJobEveryMinute_InUtc_OnQueueMarket()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        using var host = builder.Build();

        CorporateActionApplyRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").ShouldBe(["corporate-action-apply"]);
        var job = storage.GetAllEntriesFromHash("recurring-job:corporate-action-apply");
        job["Cron"].ShouldBe("* * * * *");
        job["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("market");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("CorporateActionApplyJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        var retry = typeof(CorporateActionApplyJob).GetMethod(nameof(CorporateActionApplyJob.Run))!
            .GetCustomAttributesData()
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_AppliesADueAction_AndASecondRunWhileOneHoldsTheLock_DoesNothing()
    {
        await using var db = await ScratchSchemaDatabase.CreateAsync("CorporateActionApplyJob");
        await db.ApplyAllAsync();
        var actionId = Convert.ToInt32((await db.ReadAsync($"""
            {ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])}
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-11-01', 0, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @action int = SCOPE_IDENTITY();
            INSERT INTO [InstrumentPrices] ([InstrumentId], [AsOfDate], [Price], [Source], [FetchedAt])
            VALUES (@a_instrument, '2026-11-01', 12.5, N'EODHD', SYSDATETIMEOFFSET());
            SELECT @action;
            """)).Single()[0]);
        using var host = await WorkerHostAsync(db.ConnectionString);
        var runLock = host.Services.GetRequiredService<SqlRunLock>();

        await using (var held = await runLock.TryAcquireAsync(CorporateActionApplyJob.JobId, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CorporateActionApplyJob>().Run(CancellationToken.None);

            (await StatusAsync(db, actionId)).ShouldBe(0);
        }

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CorporateActionApplyJob>().Run(CancellationToken.None);
        }

        // No account of the tenant rows is Custodial with a holding, so the action is entitled to no one and is
        // Applied in the same run it becomes effective.
        (await StatusAsync(db, actionId)).ShouldBe(2);
    }

    private static async Task<int> StatusAsync(ScratchSchemaDatabase db, int actionId) =>
        Convert.ToInt32((await db.ReadAsync($"SELECT [Status] FROM [CorporateActions] WHERE [Id] = {actionId}")).Single()[0]);

    private static async Task<IHost> WorkerHostAsync(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = connectionString;
        builder.AddInfrastructureServices();
        builder.AddMarketDataConnections(MarketDataHost.Worker);
        builder.Services.AddScoped<IUser, SystemUser>();
        var configured = new ConfiguredMarketDataConnections([]);
        builder.Services.RemoveAll<ConfiguredMarketDataConnections>();
        builder.Services.AddSingleton(configured);
        builder.Services.RemoveAll<MarketDataConnections>();
        builder.Services.AddSingleton(configured.Connections);
        var host = builder.Build();
        await host.Services.GetRequiredService<ICurrencyCatalog>().ReloadAsync(CancellationToken.None);
        return host;
    }
}
