using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Simulated instruments R7, R8: the recurring job <c>market-data-sync:{connection}</c>.</summary>
public class MarketDataSyncJobTests
{
    private const string Connection = "job-market";

    [Test]
    public void Register_AddsOneUtcJobPerConnection_OnQueueMarket_AtItsPollMinutes()
    {
        using var host = HangfireHost();
        var connections = new ConfiguredMarketDataConnections([
            Settings("simulator", ".SIM", pollMinutes: 1),
            Settings("other", ".OTH", pollMinutes: 5)
        ]);

        MarketDataRecurringJob.Register(
            host.Services.GetRequiredService<IRecurringJobManager>(), host.Services.GetRequiredService<JobStorage>(), connections);

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").OrderBy(id => id)
            .ShouldBe(["market-data-sync:other", "market-data-sync:simulator"]);
        var simulator = storage.GetAllEntriesFromHash("recurring-job:market-data-sync:simulator");
        simulator["Cron"].ShouldBe("*/1 * * * *");
        simulator["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(simulator["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("market");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("MarketDataSyncJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
        using var arguments = JsonDocument.Parse(payload.RootElement.GetProperty("Arguments").GetString()!);
        JsonSerializer.Deserialize<string>(arguments.RootElement[0].GetString()!).ShouldBe("simulator");
        storage.GetAllEntriesFromHash("recurring-job:market-data-sync:other")["Cron"].ShouldBe("*/5 * * * *");
    }

    [Test]
    public void Register_RemovesTheJobOfAConnectionNoLongerConfigured_AndLeavesOtherJobs()
    {
        using var host = HangfireHost();
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        var storage = host.Services.GetRequiredService<JobStorage>();
        recurring.AddOrUpdate(
            "bank-statement-sync:simulator", Hangfire.Common.Job.FromExpression(() => Console.WriteLine()), "*/1 * * * *", new RecurringJobOptions());
        MarketDataRecurringJob.Register(recurring, storage, new ConfiguredMarketDataConnections([
            Settings("simulator", ".SIM", 1), Settings("old", ".OLD", 1)
        ]));

        MarketDataRecurringJob.Register(recurring, storage, new ConfiguredMarketDataConnections([Settings("simulator", ".SIM", 1)]));

        using var connection = storage.GetConnection();
        connection.GetAllItemsFromSet("recurring-jobs").OrderBy(id => id)
            .ShouldBe(["bank-statement-sync:simulator", "market-data-sync:simulator"]);
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        // Read from metadata: constructing the attribute asks Hangfire's global log provider for a logger.
        var retry = typeof(MarketDataSyncJob).GetMethod(nameof(MarketDataSyncJob.Run))!
            .GetCustomAttributesData()
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_ReadsThePricesOnce_AsTheWorker_AndAFailedRead_FailsTheRun()
    {
        var prices = new ScriptedPrices();
        using var host = await WorkerHostAsync(prices);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<MarketDataSyncJob>().Run(Connection, CancellationToken.None);
            }

            prices.Calls.ShouldBe(1);
            prices.FailWith = MarketDataFailure.Unavailable;
            using (var scope = host.Services.CreateScope())
            {
                await Should.ThrowAsync<MarketDataSyncFailedException>(() =>
                    scope.ServiceProvider.GetRequiredService<MarketDataSyncJob>().Run(Connection, CancellationToken.None));
            }

            prices.Calls.ShouldBe(2);
            await using var db = OpenDb();
            var feed = await db.MarketDataFeeds.AsNoTracking()
                .SingleAsync(row => row.Connection == Connection && row.Kind == MarketDataFeedKind.Prices);
            feed.LastSucceededAt.ShouldNotBeNull();
            feed.LastFailure.ShouldBe(MarketDataFailure.Unavailable);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Test]
    public async Task Run_WhileAnotherRunOfTheConnectionHoldsTheLock_EndsAtOnce_AndDoesNothing()
    {
        var prices = new ScriptedPrices();
        using var host = await WorkerHostAsync(prices);
        var runLock = host.Services.GetRequiredService<SqlRunLock>();

        try
        {
            await using (var held = await runLock.TryAcquireAsync(MarketDataSyncJob.JobId(Connection), CancellationToken.None))
            {
                held.ShouldNotBeNull();
                using var scope = host.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MarketDataSyncJob>().Run(Connection, CancellationToken.None);

                prices.Calls.ShouldBe(0);
            }

            using (var scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<MarketDataSyncJob>().Run(Connection, CancellationToken.None);
            }

            prices.Calls.ShouldBe(1);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Test]
    public async Task Run_WhoseCorporateActionReadFails_SkipsThePriceRead_AndFailsTheRun()
    {
        var prices = new ScriptedPrices();
        var feed = new ScriptedFeed { FailWith = MarketDataFailure.Unreachable };
        using var host = await WorkerHostAsync(prices, feed);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                await Should.ThrowAsync<MarketDataSyncFailedException>(() =>
                    scope.ServiceProvider.GetRequiredService<MarketDataSyncJob>().Run(Connection, CancellationToken.None));
            }

            (feed.Calls, prices.Calls).ShouldBe((1, 0));
            await using var db = OpenDb();
            var state = await db.MarketDataFeeds.AsNoTracking()
                .SingleAsync(row => row.Connection == Connection && row.Kind == MarketDataFeedKind.CorporateActions);
            state.LastFailure.ShouldBe(MarketDataFailure.Unreachable);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private static MarketDataConnectionSettings Settings(string name, string suffix, int pollMinutes) =>
        new(name, "Simulator", suffix, new Uri("http://simulator:8082"), "key", 10, pollMinutes);

    private static IHost HangfireHost()
    {
        var builder = TestHost.CreateBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static async Task<IHost> WorkerHostAsync(IMarketPrices prices, ICorporateActionFeed? feed = null)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.AddMarketDataConnections(MarketDataHost.Worker);
        builder.Services.AddScoped<IUser, SystemUser>();
        var configured = new ConfiguredMarketDataConnections([Settings(Connection, ".JOB", 1)]);
        builder.Services.RemoveAll<ConfiguredMarketDataConnections>();
        builder.Services.AddSingleton(configured);
        builder.Services.RemoveAll<MarketDataConnections>();
        builder.Services.AddSingleton(configured.Connections);
        builder.Services.RemoveAll<IMarketPrices>();
        builder.Services.AddSingleton(prices);
        builder.Services.RemoveAll<ICorporateActionFeed>();
        builder.Services.AddSingleton(feed ?? new ScriptedFeed());
        var host = builder.Build();
        await host.Services.GetRequiredService<ICurrencyCatalog>().ReloadAsync(CancellationToken.None);
        return host;
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);

    private static async Task CleanupAsync()
    {
        await using var db = OpenDb();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [CorporateActionAnnouncements] WHERE [Connection] = {0}", Connection);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [MarketDataFeeds] WHERE [Connection] = {0}", Connection);
    }

    /// <summary>A party that answers no prices, or fails while <see cref="FailWith"/> is set.</summary>
    private sealed class ScriptedPrices : IMarketPrices
    {
        public int Calls { get; private set; }

        public MarketDataFailure? FailWith { get; set; }

        public Task<MarketPricesResult> GetPricesAsync(string connection, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(FailWith is { } failure
                ? MarketPricesResult.Failed(failure)
                : MarketPricesResult.Succeeded([]));
        }
    }

    /// <summary>A party with no announcements, or that fails while <see cref="FailWith"/> is set.</summary>
    private sealed class ScriptedFeed : ICorporateActionFeed
    {
        public int Calls { get; private set; }

        public MarketDataFailure? FailWith { get; set; }

        public Task<CorporateActionFeedResult> GetAnnouncementsAsync(
            string connection,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(FailWith is { } failure
                ? CorporateActionFeedResult.Failed(failure)
                : CorporateActionFeedResult.Succeeded(new CorporateActionFeedPage([], cursor, false)));
        }
    }
}
