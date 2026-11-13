using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Currencies;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Interceptors;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

// demo-tenant-seed R4–R12. Each test owns a scratch database with every schema script applied,
// so the demo tenant does not exist until the test creates it.
public class DemoTenantSeedTests
{
    private ScratchSchemaDatabase _database = null!;
    private CapturingLoggerProvider _logs = null!;

    [SetUp]
    public async Task CreateDatabase()
    {
        _logs = new CapturingLoggerProvider();
        _database = await ScratchSchemaDatabase.CreateAsync("demo_seed");
        await _database.ApplyAllAsync();
    }

    [TearDown]
    public async Task DropDatabase()
    {
        _logs.Dispose();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task EmptyDatabase_CreatesTheTenantTheCatalogAndBothGrants()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = (await db.Tenants.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        tenant.Code.ShouldBe("demo");
        tenant.Name.ShouldBe("Demo Advisory");
        tenant.ReportingCurrency.ShouldBe("USD");
        tenant.IsActive.ShouldBeTrue();
        tenant.CreatedBy.ShouldBe("system");

        var instruments = await db.Instruments.AsNoTracking()
            .Where(row => row.TenantId == tenant.Id)
            .ToListAsync();
        instruments.Select(row => (row.Symbol, row.Name, row.QuoteCurrency, row.IsActive))
            .OrderBy(row => row.Symbol, StringComparer.Ordinal)
            .ShouldBe(ExpectedCatalog.Select(row => (row.Symbol, row.Name, row.Quote, true))
                .OrderBy(row => row.Symbol, StringComparer.Ordinal));
        instruments.ShouldAllBe(row => row.CreatedBy == "system");

        (await GrantedCodesAsync(db, tenant.Id)).ShouldBe(["adviser-balances", "customer-transactions"]);
    }

    [Test]
    public async Task SecondRun_CreatesNoRowsAndChangesNothing()
    {
        await using var provider = BuildProvider();
        await RunAsync(provider);
        var before = await SnapshotAsync(provider);
        before.ShouldContain("|demo|");

        await RunAsync(provider);

        (await SnapshotAsync(provider)).ShouldBe(before);
    }

    [Test]
    public async Task ExistingDemoTenant_KeepsItsRowsAndAddsWhatIsMissing()
    {
        await _database.ExecuteAsync("""
            INSERT INTO [Tenants] ([PublicId], [Name], [Code], [Created], [LastModified], [ReportingCurrency], [IsActive])
            VALUES (NEWID(), N'Renamed Demo', N'demo', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'NZD', 0);
            DECLARE @t int = SCOPE_IDENTITY();
            INSERT INTO [Instruments] ([PublicId], [TenantId], [Symbol], [Name], [QuoteCurrency], [IsActive], [Created], [CreatedBy], [LastModified])
            VALUES (NEWID(), @t, N'VTI', N'Edited name', 'USD', 0, SYSDATETIMEOFFSET(), N'someone', SYSDATETIMEOFFSET());
            INSERT INTO [TenantReports] ([TenantId], [ReportId])
            SELECT @t, [Id] FROM [Reports] WHERE [Code] = N'customer-transactions';
            """);
        await using var provider = BuildProvider();

        await RunAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync();
        tenant.Name.ShouldBe("Renamed Demo");
        tenant.ReportingCurrency.ShouldBe("NZD");
        tenant.IsActive.ShouldBeFalse();

        var vti = await db.Instruments.AsNoTracking().SingleAsync(row => row.Symbol == "VTI");
        vti.Name.ShouldBe("Edited name");
        vti.IsActive.ShouldBeFalse();
        vti.CreatedBy.ShouldBe("someone");
        (await db.Instruments.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(ExpectedCatalog.Length);

        (await GrantedCodesAsync(db, tenant.Id)).ShouldBe(["adviser-balances", "customer-transactions"]);
        _logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("disabled"));
    }

    [Test]
    public async Task InactiveCatalogReport_IsNotGrantedAndIsLogged()
    {
        await _database.ExecuteAsync("UPDATE [Reports] SET [IsActive] = 0 WHERE [Code] = N'adviser-balances'");
        await using var provider = BuildProvider();

        await RunAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = (await db.Tenants.AsNoTracking().Where(row => row.Code == "demo").ToListAsync())
            .ShouldHaveSingleItem();
        (await GrantedCodesAsync(db, tenant.Id)).ShouldBe(["customer-transactions"]);
        _logs.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("adviser-balances"));
    }

    [Test]
    public async Task OtherTenant_IsUntouchedAndNothingElseIsWritten()
    {
        await _database.ExecuteAsync(ScratchSchemaDatabase.TenantRows("o", "other"));
        var before = await OtherRowCountsAsync();
        await using var provider = BuildProvider();

        await RunAsync(provider);

        (await OtherRowCountsAsync()).ShouldBe(before);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Tenants.Select(row => row.Code).OrderBy(code => code).ToListAsync())
            .ShouldBe(["demo", "other"]);
        var other = await db.Tenants.AsNoTracking().SingleAsync(row => row.Code == "other");
        (await db.Instruments.CountAsync(row => row.TenantId == other.Id)).ShouldBe(2);
        (await db.TenantReports.CountAsync(row => row.TenantId == other.Id)).ShouldBe(0);
    }

    [Test]
    public async Task FailureAfterTheTenantInsert_RollsBackTheWholeRun()
    {
        await _database.ExecuteAsync("""
            DELETE FROM [ReportSchedules];
            DELETE FROM [Reports] WHERE [Code] = N'adviser-balances';
            """);
        await using var provider = BuildProvider();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(provider));

        exception.Message.ShouldContain("adviser-balances");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Tenants.AnyAsync()).ShouldBeFalse();
        (await db.Instruments.AnyAsync()).ShouldBeFalse();
        (await db.TenantReports.AnyAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task Run_LogsWhatWasCreatedAndWhatExisted()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider);
        await RunAsync(provider);

        var summaries = _logs.Entries
            .Where(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("Demo seed:"))
            .Select(entry => entry.Message)
            .ToList();
        summaries.Count.ShouldBe(2);
        summaries[0].ShouldBe(
            "Demo seed: tenant demo created; instruments 18 created, 0 existing; reports 2 granted, 0 existing");
        summaries[1].ShouldBe(
            "Demo seed: tenant demo existing; instruments 0 created, 18 existing; reports 0 granted, 2 existing");
    }

    private static readonly (string Symbol, string Name, string Quote)[] ExpectedCatalog =
    [
        ("VTI", "Vanguard Total Stock Market ETF", "USD"),
        ("AAPL", "Apple Inc.", "USD"),
        ("MSFT", "Microsoft Corporation", "USD"),
        ("AIA", "Auckland International Airport", "NZD"),
        ("FPH", "Fisher & Paykel Healthcare", "NZD"),
        ("MEL", "Meridian Energy", "NZD"),
        ("BHP", "BHP Group", "AUD"),
        ("CBA", "Commonwealth Bank of Australia", "AUD"),
        ("WOW", "Woolworths Group", "AUD"),
        ("SAP", "SAP SE", "EUR"),
        ("AIR", "Airbus SE", "EUR"),
        ("OR", "L'Oréal", "EUR"),
        ("SHEL", "Shell plc", "GBP"),
        ("HSBA", "HSBC Holdings", "GBP"),
        ("ULVR", "Unilever", "GBP"),
        ("7203", "Toyota Motor", "JPY"),
        ("6758", "Sony Group", "JPY"),
        ("9984", "SoftBank Group", "JPY")
    ];

    private static async Task RunAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICurrencyCatalog>().ReloadAsync();
        await DemoTenantSeed.RunAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task<List<string>> GrantedCodesAsync(ApplicationDbContext db, int tenantId) =>
        await db.TenantReports.AsNoTracking()
            .Where(grant => grant.TenantId == tenantId)
            .Join(db.Reports, grant => grant.ReportId, report => report.Id, (_, report) => report.Code)
            .OrderBy(code => code)
            .ToListAsync();

    private static async Task<string> SnapshotAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenants = await db.Tenants.AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => $"{row.Id}|{row.PublicId}|{row.Code}|{row.Name}|{row.ReportingCurrency}|{row.IsActive}|{row.Created:O}")
            .ToListAsync();
        var instruments = await db.Instruments.AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => $"{row.Id}|{row.PublicId}|{row.Symbol}|{row.Name}|{row.IsActive}|{row.Created:O}")
            .ToListAsync();
        var grants = await db.TenantReports.AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => $"{row.Id}|{row.TenantId}|{row.ReportId}")
            .ToListAsync();
        return string.Join("\n", tenants.Concat(instruments).Concat(grants));
    }

    private async Task<string> OtherRowCountsAsync()
    {
        var rows = await _database.ReadAsync("""
            SELECT
                (SELECT COUNT(*) FROM [Users]),
                (SELECT COUNT(*) FROM [AspNetUsers]),
                (SELECT COUNT(*) FROM [Accounts]),
                (SELECT COUNT(*) FROM [Transactions]),
                (SELECT COUNT(*) FROM [Portfolios]),
                (SELECT COUNT(*) FROM [ReportSchedules]),
                (SELECT COUNT(*) FROM [InstrumentPrices]),
                (SELECT COUNT(*) FROM [FxRates])
            """);
        return string.Join("|", rows.Single());
    }

    // Mirrors the DbContext registration in src/Infrastructure/DependencyInjection.cs.
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUser, NoUser>();
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(_database.ConnectionString);
        });
        services.AddScoped<ITenantIdAccessor, TenantIdAccessor>();
        services.AddSingleton<ICurrencyCatalog, CurrencyCatalog>();
        return services.BuildServiceProvider();
    }

    // No signed-in caller, as in the --seed-demo command.
    private sealed class NoUser : IUser
    {
        public string? Id => null;

        public List<string>? Roles => null;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
