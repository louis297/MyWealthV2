using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.Custodian;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>In-kind transfers R9, R57: the recurring jobs <c>custodian-sync:{connection}</c> and
/// <c>custodian-reconciliation</c> on queue <c>custodian</c>.</summary>
public class CustodianJobsTests
{
    [TestCase(1, "*/1 * * * *")]
    [TestCase(15, "*/15 * * * *")]
    [TestCase(60, "0 * * * *")]
    public void Cron_StartsOnTheSameMinutesOfEachHour(int pollMinutes, string cron) =>
        CustodianSyncRecurringJob.Cron(pollMinutes).ShouldBe(cron);

    [Test]
    public void Register_AddsOneUtcJobPerConnection_OnQueueCustodian_AndRemovesAConnectionNoLongerConfigured()
    {
        using var host = HangfireHost();
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        var storage = host.Services.GetRequiredService<JobStorage>();
        CustodianSyncRecurringJob.Register(recurring, storage, new ConfiguredCustodianConnections([Settings("simulator", 1), Settings("old", 5)]));
        CustodianSyncRecurringJob.Register(recurring, storage, new ConfiguredCustodianConnections([Settings("simulator", 2)]));

        using var connection = storage.GetConnection();
        connection.GetAllItemsFromSet("recurring-jobs").ShouldBe(["custodian-sync:simulator"]);
        var job = connection.GetAllEntriesFromHash("recurring-job:custodian-sync:simulator");
        (job["Cron"], job["TimeZoneId"]).ShouldBe(("*/2 * * * *", "UTC"));
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("custodian");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("CustodianSyncJob");
    }

    [Test]
    public void TheReconciliationJob_IsDailyAt1710Utc_OnQueueCustodian()
    {
        using var host = HangfireHost();

        CustodianReconciliationRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = connection.GetAllEntriesFromHash("recurring-job:custodian-reconciliation");
        (job["Cron"], job["TimeZoneId"]).ShouldBe(("10 17 * * *", "UTC"));
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("custodian");
    }

    [TestCase(typeof(CustodianSyncJob))]
    [TestCase(typeof(CustodianReconciliationJob))]
    public void Run_HasNoAutomaticRetry(Type job)
    {
        var retry = job.GetMethod("Run")!.GetCustomAttributesData().Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts)).TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_SendsThenPullsThenProcesses()
    {
        await using var scratch = await ScratchAsync("CustodianSyncRun");
        var world = await InsertWorldAsync(scratch.ConnectionString);
        var feed = new ScriptedFeed();
        feed.Add(world.AccountNumber, "ACME", 800m);
        var transfers = new ScriptedTransfers(feed);
        using var host = await WorkerHostAsync(scratch.ConnectionString, feed, transfers);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CustodianSyncJob>().Run("simulator", CancellationToken.None);
        }

        transfers.Requests.ShouldHaveSingleItem().InstructionId.ShouldBe(world.TransferPublicId!.Value);
        transfers.FeedCallsAtFirstSend.ShouldBe(0);
        await using var db = OpenDb(scratch.ConnectionString);
        var movement = await db.CustodyMovements.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.SafekeepingAccountId == world.SafekeepingAccountId);
        (movement.Status, movement.UnmatchedReason).ShouldBe((CustodyMovementStatus.Unmatched, (CustodyUnmatchedReason?)CustodyUnmatchedReason.NoInstruction));
        movement.CreatedBy.ShouldBe(SystemUser.Name);
        (await db.InKindTransfers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == world.TransferPublicId))
            .Status.ShouldBe(InKindTransferStatus.Sent);
    }

    [Test]
    public async Task Run_WithAFailedAccount_ProcessesWhatIsStored_ThenFails()
    {
        await using var scratch = await ScratchAsync("CustodianSyncFailed");
        var world = await InsertWorldAsync(scratch.ConnectionString, transfer: false);
        var feed = new ScriptedFeed();
        feed.Add(world.AccountNumber, "ACME", 5m);
        using var host = await WorkerHostAsync(scratch.ConnectionString, feed, new ScriptedTransfers(feed));
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PullCustodyMovements>().RunAsync("simulator");
        }

        feed.FailWith = CustodianFailure.Unavailable;
        using (var scope = host.Services.CreateScope())
        {
            await Should.ThrowAsync<CustodianSyncFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<CustodianSyncJob>().Run("simulator", CancellationToken.None));
        }

        await using var db = OpenDb(scratch.ConnectionString);
        (await db.CustodyMovements.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.SafekeepingAccountId == world.SafekeepingAccountId))
            .Status.ShouldBe(CustodyMovementStatus.Unmatched);
    }

    [Test]
    public async Task Run_WhileAnotherRunOfTheConnectionHoldsTheLock_EndsAtOnce()
    {
        await using var scratch = await ScratchAsync("CustodianSyncLock");
        var feed = new ScriptedFeed();
        using var host = await WorkerHostAsync(scratch.ConnectionString, feed, new ScriptedTransfers(feed));
        var runLock = host.Services.GetRequiredService<SqlRunLock>();
        await using var held = await runLock.TryAcquireAsync("custodian-sync:simulator", CancellationToken.None);
        held.ShouldNotBeNull();

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CustodianSyncJob>().Run("simulator", CancellationToken.None);

        feed.Calls.ShouldBe(0);
    }

    [Test]
    public async Task TheReconciliationJob_RunsEveryActiveSafekeepingAccount_ADisabledTenantsIncluded()
    {
        await using var scratch = await ScratchAsync("CustodianReconciliationJob");
        var world = await InsertWorldAsync(scratch.ConnectionString, transfer: false, disableTenant: true);
        var feed = new ScriptedFeed();
        using var host = await WorkerHostAsync(scratch.ConnectionString, feed, new ScriptedTransfers(feed));

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CustodianReconciliationJob>().Run(CancellationToken.None);
        }

        await using var db = OpenDb(scratch.ConnectionString);
        var run = await db.CustodianReconciliations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.SafekeepingAccountId == world.SafekeepingAccountId);
        (run.Trigger, run.RanByUserId, run.Status).ShouldBe((ReconciliationTrigger.Scheduled, (int?)null, CustodianReconciliationStatus.NoPositions));
        run.CreatedBy.ShouldBe(SystemUser.Name);
    }

    private static CustodianConnectionSettings Settings(string name, int pollMinutes) =>
        new(name, "Simulator", new Uri("http://simulator:8082"), "key", 10, pollMinutes);

    private static IHost HangfireHost()
    {
        var builder = TestHost.CreateBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static async Task<ScratchSchemaDatabase> ScratchAsync(string name)
    {
        var scratch = await ScratchSchemaDatabase.CreateAsync(name);
        await scratch.ApplyAllAsync();
        return scratch;
    }

    private static async Task<IHost> WorkerHostAsync(string connectionString, ScriptedFeed feed, ScriptedTransfers transfers)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = connectionString;
        builder.AddInfrastructureServices();
        builder.AddBankConnections(BankConnectionHost.Worker);
        builder.AddBrokerConnections(BrokerConnectionHost.Worker);
        builder.AddCustodianConnections(CustodianConnectionHost.Worker);
        builder.Services.AddScoped<IUser, SystemUser>();
        builder.Services.RemoveAll<ICustodianConnections>();
        builder.Services.AddSingleton<ICustodianConnections>(new ConfiguredCustodianConnections([Settings("simulator", 1)]));
        builder.Services.RemoveAll<ICustodianFeed>();
        builder.Services.AddSingleton<ICustodianFeed>(feed);
        builder.Services.RemoveAll<ICustodianTransfers>();
        builder.Services.AddSingleton<ICustodianTransfers>(transfers);
        var host = builder.Build();
        await host.Services.GetRequiredService<ICurrencyCatalog>().ReloadAsync(CancellationToken.None);
        return host;
    }

    private sealed record World(string AccountNumber, int SafekeepingAccountId, Guid? TransferPublicId);

    /// <summary>A tenant with an active USD safekeeping account on <c>simulator</c> and, optionally, an approved transfer
    /// in of 100 ACME on an Open Custodial USD account.</summary>
    private static async Task<World> InsertWorldAsync(string connectionString, bool transfer = true, bool disableTenant = false)
    {
        await using var db = OpenDb(connectionString);
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var (adviser, otherAdviser) = await TwoTenantFixture.InsertAdvisersAsync(db, tenant, otherTenant);
        var (customer, _) = await TwoTenantFixture.InsertCustomersAsync(db, tenant, otherTenant, adviser, otherAdviser);
        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD");
        var number = "SKJ-" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var safekeeping = SafekeepingAccount.Create(tenant.Id, usd, "Simulator Custody", "Nominees", number, "simulator");
        safekeeping.CreatedBy = "test";
        db.SafekeepingAccounts.Add(safekeeping);
        await db.SaveChangesAsync();
        Guid? transferId = null;
        if (transfer)
        {
            var account = Account.Create(tenant.Id, customer.Id, "Managed", AccountType.Brokerage, CustodyMode.Custodial, usd,
                DepositReference.Generate(CryptoRandomSymbols.Instance));
            account.CreatedBy = "test";
            var instrument = Instrument.Create(tenant.Id, "ACME", "Acme", usd);
            instrument.CreatedBy = "test";
            db.Accounts.Add(account);
            db.Instruments.Add(instrument);
            await db.SaveChangesAsync();
            var row = InKindTransfer.Request(tenant.Id, account.Id, instrument.Id, TransferDirection.In, 100m, "USD", 9500m, null, false,
                "Harbour Brokers", "HB-0044172", "J Smith", null, adviser.Id, DateTimeOffset.UtcNow);
            row.Approve(customer.Id, DateTimeOffset.UtcNow, null, safekeeping.Id);
            row.CreatedBy = "test";
            db.InKindTransfers.Add(row);
            await db.SaveChangesAsync();
            transferId = row.PublicId;
        }

        if (disableTenant)
        {
            (await db.Tenants.SingleAsync(row => row.Id == tenant.Id)).Disable();
            await db.SaveChangesAsync();
        }

        return new World(number, safekeeping.Id, transferId);
    }

    private static ApplicationDbContext OpenDb(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);

    /// <summary>A feed with movements for one account; every other account has none and no positions.</summary>
    private sealed class ScriptedFeed : ICustodianFeed
    {
        private readonly Dictionary<string, List<CustodianFeedMovement>> _movements = new(StringComparer.OrdinalIgnoreCase);

        public int Calls { get; private set; }

        public CustodianFailure? FailWith { get; set; }

        public void Add(string accountNumber, string symbol, decimal quantity)
        {
            if (!_movements.TryGetValue(accountNumber, out var movements))
            {
                _movements[accountNumber] = movements = [];
            }

            movements.Add(new CustodianFeedMovement("job-" + Guid.NewGuid().ToString("N"), CustodyMovementDirection.In,
                CustodyMovementKind.Transfer, symbol, "USD", quantity, new DateOnly(2026, 11, 24)));
        }

        public Task<CustodianFeedResult> GetMovementsAsync(CustodianFeedRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailWith is { } failure && _movements.ContainsKey(request.AccountNumber))
            {
                return Task.FromResult(CustodianFeedResult.Failed(failure));
            }

            var movements = _movements.GetValueOrDefault(request.AccountNumber) ?? [];
            var after = request.Cursor is null ? 0 : int.Parse(request.Cursor);
            var page = movements.Skip(after).ToList();
            return Task.FromResult(CustodianFeedResult.Succeeded(new CustodianFeedPage(page, (after + page.Count).ToString(), false, [])));
        }
    }

    /// <summary>A custodian that accepts every instruction and notes how often the feed was read before its first call.</summary>
    private sealed class ScriptedTransfers(ScriptedFeed feed) : ICustodianTransfers
    {
        public List<CustodianTransferRequest> Requests { get; } = [];

        public int? FeedCallsAtFirstSend { get; private set; }

        public Task<CustodianTransferResult> SendAsync(CustodianTransferRequest request, CancellationToken cancellationToken)
        {
            FeedCallsAtFirstSend ??= feed.Calls;
            Requests.Add(request);
            return Task.FromResult(CustodianTransferResult.Accepted("job-T" + Requests.Count));
        }
    }
}
