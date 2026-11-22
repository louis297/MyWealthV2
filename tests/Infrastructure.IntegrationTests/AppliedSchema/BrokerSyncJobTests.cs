using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.BankFeed;
using MyWealthV2.Infrastructure.Broker;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Orders R19: the recurring job <c>broker-sync:{connection}</c>. The runs write instruments, holdings and
/// trades, so they use a database of their own and leave the shared one as the schema scripts made it.</summary>
public class BrokerSyncJobTests
{
    private static ScratchSchemaDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        _database = await ScratchSchemaDatabase.CreateAsync("BrokerJob");
        await _database.ApplyAllAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase() => await _database.DisposeAsync();

    [TestCase(1, "*/1 * * * *")]
    [TestCase(5, "*/5 * * * *")]
    [TestCase(60, "0 * * * *")]
    public void Cron_StartsOnTheSameMinutesOfEachHour(int pollMinutes, string cron) =>
        BrokerSyncRecurringJob.Cron(pollMinutes).ShouldBe(cron);

    [Test]
    public void Register_AddsOneUtcJobPerConnection_OnQueueBroker_AtItsPollMinutes()
    {
        using var host = HangfireHost();
        var connections = new ConfiguredBrokerConnections([Settings("simulator", pollMinutes: 1), Settings("other-broker", pollMinutes: 5)]);

        BrokerSyncRecurringJob.Register(
            host.Services.GetRequiredService<IRecurringJobManager>(), host.Services.GetRequiredService<JobStorage>(), connections);

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").OrderBy(id => id).ShouldBe(["broker-sync:other-broker", "broker-sync:simulator"]);
        var simulator = storage.GetAllEntriesFromHash("recurring-job:broker-sync:simulator");
        simulator["Cron"].ShouldBe("*/1 * * * *");
        simulator["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(simulator["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("broker");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("BrokerSyncJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
        using var arguments = JsonDocument.Parse(payload.RootElement.GetProperty("Arguments").GetString()!);
        JsonSerializer.Deserialize<string>(arguments.RootElement[0].GetString()!).ShouldBe("simulator");
        storage.GetAllEntriesFromHash("recurring-job:broker-sync:other-broker")["Cron"].ShouldBe("*/5 * * * *");
    }

    [Test]
    public void Register_RemovesTheJobOfAConnectionNoLongerConfigured_AndLeavesOtherJobsAlone()
    {
        using var host = HangfireHost();
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        var storage = host.Services.GetRequiredService<JobStorage>();
        BankStatementRecurringJob.Register(recurring, storage, new ConfiguredBankConnections([
            new BankConnectionSettings("simulator", "Simulator", new Uri("http://simulator:8082"), "key", 10, 1)
        ]));
        BrokerSyncRecurringJob.Register(recurring, storage, new ConfiguredBrokerConnections([Settings("simulator", 1), Settings("old-broker", 1)]));

        BrokerSyncRecurringJob.Register(recurring, storage, new ConfiguredBrokerConnections([Settings("simulator", 1)]));

        using var connection = storage.GetConnection();
        connection.GetAllItemsFromSet("recurring-jobs").OrderBy(id => id)
            .ShouldBe(["bank-statement-sync:simulator", "broker-sync:simulator"]);
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        // Read from metadata: constructing the attribute asks Hangfire's global log provider for a logger.
        var retry = typeof(BrokerSyncJob).GetMethod(nameof(BrokerSyncJob.Run))!
            .GetCustomAttributesData()
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_SendsTheOrder_ThenPullsProcessesAndPostsItsFill_InOneRun_AsTheWorker()
    {
        var world = await InsertWorldAsync();
        var broker = new ScriptedBroker();
        broker.FillWhenPlaced(world.OrderPublicId, 10m, 101.50m);
        using var host = await WorkerHostAsync(broker, world.Connection);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BrokerSyncJob>().Run(world.Connection, CancellationToken.None);
        }

        broker.Calls.ShouldBe(["place", "reports"]);
        var order = await OrderAsync(world);
        await using var db = OpenDb();
        (order.Status, order.BrokerOrderId, order.ReservedCash, order.LastModifiedBy).ShouldBe((OrderStatus.Filled, "job-O1", 0m, SystemUser.Name));
        var fill = order.Fills.ShouldHaveSingleItem();
        fill.Status.ShouldBe(OrderFillStatus.Posted);
        var trade = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == fill.TransactionId);
        (trade.SourceKind, trade.SourcePublicId).ShouldBe((TransactionSourceKind.OrderFill, (Guid?)world.OrderPublicId));
        (trade.Type, trade.CreatedBy).ShouldBe((TransactionType.Buy, SystemUser.Name));
        var holding = await db.Holdings.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.AccountId == order.AccountId);
        (holding.Quantity, holding.CostAmount, holding.CreatedBy).ShouldBe((10m, 1015.00m, SystemUser.Name));
        (await db.ExecutionReports.AsNoTracking().SingleAsync(row => row.ClientOrderId == world.OrderPublicId.ToString()))
            .Status.ShouldBe(ExecutionReportStatus.Processed);
    }

    [Test]
    public async Task Run_WithAFailedPull_StillSendsTheOrder_AndProcessesWhatIsStored_ThenFails()
    {
        var world = await InsertWorldAsync();
        var broker = new ScriptedBroker();
        using var host = await WorkerHostAsync(broker, world.Connection);
        using (var scope = host.Services.CreateScope())
        {
            // A report stored by an earlier run that did not process it.
            broker.Add(world.OrderPublicId, 4m, 100m);
            await scope.ServiceProvider.GetRequiredService<PullExecutionReports>().RunAsync(world.Connection);
        }

        broker.FeedFailure = BrokerFailure.Unavailable;

        BrokerSyncFailedException failure;
        using (var scope = host.Services.CreateScope())
        {
            failure = await Should.ThrowAsync<BrokerSyncFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<BrokerSyncJob>().Run(world.Connection, CancellationToken.None));
        }

        failure.Message.ShouldContain("pull: Unavailable");
        var order = await OrderAsync(world);
        await using var db = OpenDb();
        (order.Status, order.BrokerOrderId, order.FilledQuantity).ShouldBe((OrderStatus.PartlyFilled, "job-O1", 4m));
        order.Fills.ShouldHaveSingleItem().Status.ShouldBe(OrderFillStatus.Posted);
        (await db.BrokerFeeds.AsNoTracking().SingleAsync(row => row.Connection == world.Connection)).LastFailure
            .ShouldBe(BrokerFailure.Unavailable);
    }

    [Test]
    public async Task Run_WithAnOrderTheBrokerDoesNotAnswer_PullsAndProcessesAllTheSame_ThenFails()
    {
        var world = await InsertWorldAsync();
        var broker = new ScriptedBroker { PlaceFailure = BrokerFailure.Timeout };
        broker.Add(world.OrderPublicId, 10m, 100m);
        using var host = await WorkerHostAsync(broker, world.Connection);

        BrokerSyncFailedException failure;
        using (var scope = host.Services.CreateScope())
        {
            failure = await Should.ThrowAsync<BrokerSyncFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<BrokerSyncJob>().Run(world.Connection, CancellationToken.None));
        }

        failure.Message.ShouldContain("send: 1 order(s) not sent");
        broker.Calls.ShouldBe(["place", "reports"]);
        // R28: the fill is booked on the order although the broker's answer never arrived.
        var order = await OrderAsync(world);
        (order.Status, order.FilledQuantity).ShouldBe((OrderStatus.Filled, 10m));
        order.Fills.ShouldHaveSingleItem().Status.ShouldBe(OrderFillStatus.Posted);
    }

    [Test]
    public async Task Run_WhileAnotherRunOfTheConnectionHoldsTheLock_EndsAtOnce_AndDoesNothing()
    {
        var world = await InsertWorldAsync();
        var broker = new ScriptedBroker();
        using var host = await WorkerHostAsync(broker, world.Connection);
        var runLock = host.Services.GetRequiredService<SqlRunLock>();
        await using (var held = await runLock.TryAcquireAsync(BrokerSyncJob.JobId(world.Connection), CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var other = await runLock.TryAcquireAsync("broker-sync:other-broker", CancellationToken.None);
            await other.ShouldNotBeNull().DisposeAsync();

            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BrokerSyncJob>().Run(world.Connection, CancellationToken.None);

            broker.Calls.ShouldBeEmpty();
        }

        (await OrderAsync(world)).Status.ShouldBe(OrderStatus.Placed);
        await using (var again = await runLock.TryAcquireAsync(BrokerSyncJob.JobId(world.Connection), CancellationToken.None))
        {
            again.ShouldNotBeNull();
        }
    }

    private static BrokerConnectionSettings Settings(string name, int pollMinutes) =>
        new(name, "Simulator", new Uri("http://simulator:8082"), "key", 10, pollMinutes);

    private static IHost HangfireHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static async Task<IHost> WorkerHostAsync(ScriptedBroker broker, string connection)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = _database.ConnectionString;
        builder.AddInfrastructureServices();
        builder.AddBrokerConnections(BrokerConnectionHost.Worker);
        builder.Services.AddScoped<IUser, SystemUser>();
        builder.Services.RemoveAll<IBrokerConnections>();
        builder.Services.AddSingleton<IBrokerConnections>(new ConfiguredBrokerConnections([Settings(connection, 1)]));
        builder.Services.RemoveAll<IBrokerOrders>();
        builder.Services.AddSingleton<IBrokerOrders>(broker);
        builder.Services.RemoveAll<IBrokerFeed>();
        builder.Services.AddSingleton<IBrokerFeed>(broker);
        var host = builder.Build();
        await host.Services.GetRequiredService<ICurrencyCatalog>().ReloadAsync(CancellationToken.None);
        return host;
    }

    private sealed record World(Guid OrderPublicId, string Connection);

    private static async Task<Order> OrderAsync(World world)
    {
        await using var db = OpenDb();
        return await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(row => row.Fills)
            .SingleAsync(row => row.PublicId == world.OrderPublicId);
    }

    /// <summary>A tenant with an Open Custodial USD account holding 5 000.00 and a <c>Placed</c> buy order of 10 at a
    /// reference price of 100.00, with no fee schedule, on a broker connection of its own, so a run meets no order,
    /// report or cursor of another test.</summary>
    private static async Task<World> InsertWorldAsync()
    {
        var connection = "job-" + Guid.NewGuid().ToString("N")[..12];
        await using var db = OpenDb();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var (adviser, otherAdviser) = await TwoTenantFixture.InsertAdvisersAsync(db, tenant, otherTenant);
        var (customer, _) = await TwoTenantFixture.InsertCustomersAsync(db, tenant, otherTenant, adviser, otherAdviser);
        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD");
        var account = Account.Create(
            tenant.Id, customer.Id, "Managed", AccountType.Brokerage, CustodyMode.Custodial, usd,
            DepositReference.Generate(CryptoRandomSymbols.Instance));
        account.CreatedBy = "test";
        var instrument = Instrument.Create(tenant.Id, "JOB", "Job Holdings", usd);
        instrument.CreatedBy = "test";
        db.Accounts.Add(account);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var funding = Transaction.Post(
            tenant.Id, account.Id, TransactionType.TransferIn, 5000m, account.Currency, now.AddMinutes(-5), memo: null,
            reference: null, TransactionSource.Manual, currentCashSum: 0m, existingTransactionCount: 0);
        funding.CreatedBy = "test";
        funding.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        var order = Order.Place(
            tenant.Id, account.Id, instrument.Id, OrderSide.Buy, 10m, "USD", 100m, DateOnly.FromDateTime(now.UtcDateTime),
            OrderFeeTerms.None, connection, memo: null, adviser.Id, now);
        order.CreatedBy = "test";
        db.Transactions.Add(funding);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return new World(order.PublicId, connection);
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_database.ConnectionString).Options);

    /// <summary>A broker that accepts every order and keeps one feed; it notes the order of its calls.</summary>
    private sealed class ScriptedBroker : IBrokerOrders, IBrokerFeed
    {
        private readonly List<BrokerFeedItem> _reports = [];
        private readonly Dictionary<string, (decimal Quantity, decimal Price)> _fillWhenPlaced = [];

        public List<string> Calls { get; } = [];

        public BrokerFailure? PlaceFailure { get; set; }

        public BrokerFailure? FeedFailure { get; set; }

        /// <summary>The broker fills the order as it takes it, as the simulator does in mode <c>Fill</c>.</summary>
        public void FillWhenPlaced(Guid orderId, decimal quantity, decimal price) =>
            _fillWhenPlaced[orderId.ToString()] = (quantity, price);

        public void Add(Guid orderId, decimal quantity, decimal price) => Add(orderId.ToString(), quantity, price);

        private void Add(string clientOrderId, decimal quantity, decimal price)
        {
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            _reports.Add(new BrokerFeedItem(
                "job-X" + Guid.NewGuid().ToString("N"), ExecutionReportKind.Fill, clientOrderId, "job-O1", quantity, price, day,
                day.AddDays(2), "CN-1", null, null, null));
        }

        public Task<BrokerPlaceResult> PlaceAsync(string connection, BrokerOrderRequest order, CancellationToken cancellationToken)
        {
            Calls.Add("place");
            if (PlaceFailure is { } failure)
            {
                return Task.FromResult(BrokerPlaceResult.Failed(failure));
            }

            if (_fillWhenPlaced.TryGetValue(order.ClientOrderId, out var fill))
            {
                Add(order.ClientOrderId, fill.Quantity, fill.Price);
            }

            return Task.FromResult(BrokerPlaceResult.Accepted("job-O1"));
        }

        public Task<BrokerCancelResult> CancelAsync(string connection, string clientOrderId, CancellationToken cancellationToken)
        {
            Calls.Add("cancel");
            return Task.FromResult(BrokerCancelResult.Answered(BrokerCancelAnswer.Accepted));
        }

        public Task<BrokerFeedResult> GetReportsAsync(string connection, string? cursor, int limit, CancellationToken cancellationToken)
        {
            Calls.Add("reports");
            if (FeedFailure is { } failure)
            {
                return Task.FromResult(BrokerFeedResult.Failed(failure));
            }

            var after = cursor is null ? 0 : int.Parse(cursor);
            var page = _reports.Skip(after).ToList();
            return Task.FromResult(BrokerFeedResult.Succeeded(new BrokerFeedPage(page, (after + page.Count).ToString(), HasMore: false)));
        }
    }
}
