using System.Reflection;
using System.Text.Json;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
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

/// <summary>Deposit matching R6, R7, R13: the recurring job <c>bank-statement-sync:{connection}</c>.</summary>
public class BankStatementSyncJobTests
{
    [TestCase(1, "*/1 * * * *")]
    [TestCase(5, "*/5 * * * *")]
    [TestCase(15, "*/15 * * * *")]
    [TestCase(60, "0 * * * *")]
    public void Cron_StartsOnTheSameMinutesOfEachHour(int pollMinutes, string cron) =>
        BankStatementRecurringJob.Cron(pollMinutes).ShouldBe(cron);

    [Test]
    public void Register_AddsOneUtcJobPerConnection_OnQueueBank_AtItsPollMinutes()
    {
        using var host = HangfireHost();
        var connections = new ConfiguredBankConnections([
            Settings("simulator", pollMinutes: 1),
            Settings("other-bank", pollMinutes: 5)
        ]);

        BankStatementRecurringJob.Register(
            host.Services.GetRequiredService<IRecurringJobManager>(), host.Services.GetRequiredService<JobStorage>(), connections);

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").OrderBy(id => id)
            .ShouldBe(["bank-statement-sync:other-bank", "bank-statement-sync:simulator"]);
        var simulator = storage.GetAllEntriesFromHash("recurring-job:bank-statement-sync:simulator");
        simulator["Cron"].ShouldBe("*/1 * * * *");
        simulator["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(simulator["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("bank");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("BankStatementSyncJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
        using var arguments = JsonDocument.Parse(payload.RootElement.GetProperty("Arguments").GetString()!);
        JsonSerializer.Deserialize<string>(arguments.RootElement[0].GetString()!).ShouldBe("simulator");
        storage.GetAllEntriesFromHash("recurring-job:bank-statement-sync:other-bank")["Cron"].ShouldBe("*/5 * * * *");
    }

    [Test]
    public void Register_RemovesTheJobOfAConnectionNoLongerConfigured()
    {
        using var host = HangfireHost();
        var recurring = host.Services.GetRequiredService<IRecurringJobManager>();
        var storage = host.Services.GetRequiredService<JobStorage>();
        BankStatementRecurringJob.Register(recurring, storage, new ConfiguredBankConnections([
            Settings("simulator", 1), Settings("old-bank", 1)
        ]));

        BankStatementRecurringJob.Register(recurring, storage, new ConfiguredBankConnections([Settings("simulator", 1)]));

        using var connection = storage.GetConnection();
        connection.GetAllItemsFromSet("recurring-jobs").ShouldBe(["bank-statement-sync:simulator"]);
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        // Read from metadata: constructing the attribute asks Hangfire's global log provider for a logger.
        var retry = typeof(BankStatementSyncJob).GetMethod(nameof(BankStatementSyncJob.Run))!
            .GetCustomAttributesData()
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_PullsThenProcesses_AsTheWorker()
    {
        var world = await InsertWorldAsync();
        var feed = new ScriptedFeed();
        var outsideId = feed.Add(world.ClientMoneyAccountNumber, 75m, world.DepositReference);
        using var host = await WorkerHostAsync(feed);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BankStatementSyncJob>().Run("simulator", CancellationToken.None);
        }

        await using var db = OpenDb();
        var line = await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.OutsideId == outsideId);
        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.CreatedBy.ShouldBe(SystemUser.Name);
        var deposit = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.SourceKind == TransactionSourceKind.BankFeed && row.SourcePublicId == line.PublicId);
        deposit.CreatedBy.ShouldBe(SystemUser.Name);
    }

    [Test]
    public async Task Run_SendsPendingPayments_BeforeItPulls()
    {
        var world = await InsertWorldAsync(withApprovedWithdrawal: true);
        var feed = new ScriptedFeed();
        var payments = new ScriptedPayments(feed);
        using var host = await WorkerHostAsync(feed, payments);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BankStatementSyncJob>().Run("simulator", CancellationToken.None);
        }

        payments.Requests.ShouldHaveSingleItem().InstructionId.ShouldBe(world.WithdrawalPublicId!.Value);
        payments.FeedCallsAtFirstSend.ShouldBe(0);
        feed.Calls.ShouldBeGreaterThan(0);
        await using var db = OpenDb();
        (await db.Withdrawals.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == world.WithdrawalPublicId))
            .Status.ShouldBe(WithdrawalStatus.Sent);
    }

    [Test]
    public async Task Run_WithAFailedAccount_ProcessesWhatIsStored_ThenFails()
    {
        var world = await InsertWorldAsync();
        var feed = new ScriptedFeed();
        var outsideId = feed.Add(world.ClientMoneyAccountNumber, 75m, world.DepositReference);
        using var host = await WorkerHostAsync(feed);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PullBankStatements>().RunAsync("simulator");
        }

        feed.FailWith = BankFeedFailure.Unavailable;

        using (var scope = host.Services.CreateScope())
        {
            await Should.ThrowAsync<BankStatementSyncFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<BankStatementSyncJob>().Run("simulator", CancellationToken.None));
        }

        await using var db = OpenDb();
        (await db.BankStatementLines.IgnoreQueryFilters().SingleAsync(row => row.OutsideId == outsideId))
            .Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task Run_WhileAnotherRunOfTheConnectionHoldsTheLock_EndsAtOnce_AndDoesNothing()
    {
        var feed = new ScriptedFeed();
        using var host = await WorkerHostAsync(feed);
        var runLock = host.Services.GetRequiredService<SqlRunLock>();
        await using (var held = await runLock.TryAcquireAsync("bank-statement-sync:simulator", CancellationToken.None))
        {
            held.ShouldNotBeNull();
            (await runLock.TryAcquireAsync("bank-statement-sync:simulator", CancellationToken.None)).ShouldBeNull();
            var other = await runLock.TryAcquireAsync("bank-statement-sync:other-bank", CancellationToken.None);
            await other.ShouldNotBeNull().DisposeAsync();

            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BankStatementSyncJob>().Run("simulator", CancellationToken.None);

            feed.Calls.ShouldBe(0);
        }

        await using (var again = await runLock.TryAcquireAsync("bank-statement-sync:simulator", CancellationToken.None))
        {
            again.ShouldNotBeNull();
        }
    }

    private static BankConnectionSettings Settings(string name, int pollMinutes) =>
        new(name, "Simulator", new Uri("http://simulator:8082"), "key", 10, pollMinutes);

    private static IHost HangfireHost()
    {
        var builder = TestHost.CreateBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static async Task<IHost> WorkerHostAsync(ScriptedFeed feed, IBankPayments? payments = null)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.AddBankConnections(BankConnectionHost.Worker);
        builder.AddBrokerConnections(BrokerConnectionHost.Worker);
        builder.Services.AddScoped<IUser, SystemUser>();
        builder.Services.RemoveAll<IBankConnections>();
        builder.Services.AddSingleton<IBankConnections>(new ConfiguredBankConnections([Settings("simulator", 1)]));
        builder.Services.RemoveAll<IBankFeed>();
        builder.Services.AddSingleton<IBankFeed>(feed);
        builder.Services.RemoveAll<IBankPayments>();
        builder.Services.AddSingleton(payments ?? new ScriptedPayments(feed));
        var host = builder.Build();
        await host.Services.GetRequiredService<ICurrencyCatalog>().ReloadAsync(CancellationToken.None);
        return host;
    }

    private sealed record World(string ClientMoneyAccountNumber, string DepositReference, Guid? WithdrawalPublicId = null);

    /// <summary>A tenant with a USD client money account and an Open Custodial USD account, and optionally an
    /// approved withdrawal from it with its Pending outgoing payment.</summary>
    private static async Task<World> InsertWorldAsync(bool withApprovedWithdrawal = false)
    {
        await using var db = OpenDb();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var (adviser, otherAdviser) = await TwoTenantFixture.InsertAdvisersAsync(db, tenant, otherTenant);
        var (customer, _) = await TwoTenantFixture.InsertCustomersAsync(db, tenant, otherTenant, adviser, otherAdviser);
        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD");
        var reference = DepositReference.Generate(CryptoRandomSymbols.Instance);
        var account = Account.Create(tenant.Id, customer.Id, "Managed", AccountType.Brokerage, CustodyMode.Custodial, usd, reference);
        account.CreatedBy = "test";
        var number = "JOB-" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var clientMoney = ClientMoneyAccount.Create(tenant.Id, usd, "Simulator Bank", "Trust", number, "simulator");
        clientMoney.CreatedBy = "test";
        db.Accounts.Add(account);
        db.ClientMoneyAccounts.Add(clientMoney);
        await db.SaveChangesAsync();
        if (!withApprovedWithdrawal)
        {
            return new World(number, reference.Value);
        }

        var now = DateTimeOffset.UtcNow;
        var nominated = NominatedBankAccount.Add(tenant.Id, customer.Id, usd, "ANZ", "J Smith", "06-0123-0456789-00", adviser.Id, now);
        nominated.Verify(customer.Id, "Checked", now);
        nominated.CreatedBy = "test";
        db.NominatedBankAccounts.Add(nominated);
        await db.SaveChangesAsync();
        var withdrawal = Withdrawal.Request(tenant.Id, account.Id, nominated.Id, 10m, "USD", reference.Value, null, adviser.Id, now);
        withdrawal.Approve(customer.Id, null, now);
        withdrawal.CreatedBy = "test";
        db.Withdrawals.Add(withdrawal);
        await db.SaveChangesAsync();
        var payment = OutgoingPayment.ForWithdrawal(withdrawal, clientMoney);
        payment.CreatedBy = "test";
        db.OutgoingPayments.Add(payment);
        await db.SaveChangesAsync();
        return new World(number, reference.Value, withdrawal.PublicId);
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);

    /// <summary>A feed with lines for one account; every other account has an empty statement.</summary>
    private sealed class ScriptedFeed : IBankFeed
    {
        private readonly Dictionary<string, List<BankFeedLine>> _lines = new(StringComparer.OrdinalIgnoreCase);

        public int Calls { get; private set; }

        public BankFeedFailure? FailWith { get; set; }

        public string Add(string accountNumber, decimal amount, string reference)
        {
            var id = "job-" + Guid.NewGuid().ToString("N");
            if (!_lines.TryGetValue(accountNumber, out var lines))
            {
                _lines[accountNumber] = lines = [];
            }

            lines.Add(new BankFeedLine(id, new DateOnly(2026, 11, 20), BankLineDirection.Credit, amount, "USD", "J SMITH", null, reference));
            return id;
        }

        public Task<BankFeedResult> GetStatementLinesAsync(BankFeedRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailWith is { } failure && _lines.ContainsKey(request.AccountNumber))
            {
                return Task.FromResult(BankFeedResult.Failed(failure));
            }

            var lines = _lines.GetValueOrDefault(request.AccountNumber) ?? [];
            var after = request.Cursor is null ? 0 : int.Parse(request.Cursor);
            var page = lines.Skip(after).ToList();
            return Task.FromResult(BankFeedResult.Succeeded(
                new BankFeedPage(page, (after + page.Count).ToString(), HasMore: false)));
        }
    }

    /// <summary>A bank that accepts every payment and notes how often the feed was read before its first call.</summary>
    private sealed class ScriptedPayments(ScriptedFeed feed) : IBankPayments
    {
        public List<BankPaymentRequest> Requests { get; } = [];

        public int? FeedCallsAtFirstSend { get; private set; }

        public Task<BankPaymentResult> SendAsync(BankPaymentRequest request, CancellationToken cancellationToken)
        {
            FeedCallsAtFirstSend ??= feed.Calls;
            Requests.Add(request);
            return Task.FromResult(BankPaymentResult.Accepted("job-P" + Requests.Count));
        }
    }
}
