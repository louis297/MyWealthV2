using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class InstrumentPriceSyncJobTests
{
    [Test]
    public async Task Sync_SourceFailure_LeavesPreviousRows()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var instrument = await AddInstrumentAsync(db, tenant, "VTI", "USD", active: true);
        var fetchedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        db.InstrumentPrices.Add(Stored(instrument.Id, 250m, new DateOnly(2026, 11, 1), fetchedAt));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var job = Job(
            db,
            new FixedCatalog(new ActiveInstrument(instrument.Id, "VTI", "USD")),
            new ThrowingSource(),
            new FixedTime(new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero)));

        try
        {
            await job.Sync();

            var row = await db.InstrumentPrices.AsNoTracking().SingleAsync(price => price.InstrumentId == instrument.Id);
            row.Price.ShouldBe(250m);
            row.FetchedAt.ShouldBe(fetchedAt);
            row.AsOfDate.ShouldBe(new DateOnly(2026, 11, 1));
        }
        finally
        {
            await CleanupAsync(db, tenant.Id, otherTenant.Id);
        }
    }

    [Test]
    public async Task Sync_WritesOneFetchOntoEveryInstrumentWithTheSameSymbol()
    {
        await using var db = Open();
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var first = await AddInstrumentAsync(db, tenantA, "VTI", "USD", active: true);
        var second = await AddInstrumentAsync(db, tenantB, "vti", "usd", active: true);
        var fetchedAt = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
        var source = new RecordingSource(new DateOnly(2026, 11, 1), price: 251m, currency: "USD");
        var job = Job(
            db,
            new FixedCatalog(
                new ActiveInstrument(first.Id, "VTI", "USD"),
                new ActiveInstrument(second.Id, "vti", "usd")),
            source,
            new FixedTime(fetchedAt));

        try
        {
            await job.Sync();

            source.Calls.ShouldBe([("VTI", "USD")]);
            var rows = await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == first.Id || row.InstrumentId == second.Id)
                .OrderBy(row => row.InstrumentId)
                .ToListAsync();
            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(row =>
                row.Price == 251m
                && row.AsOfDate == new DateOnly(2026, 11, 1)
                && row.FetchedAt == fetchedAt
                && row.Source == "EODHD");
        }
        finally
        {
            await CleanupAsync(db, tenantA.Id, tenantB.Id);
        }
    }

    [Test]
    public async Task Sync_SkipsARejectedSymbolAndStillWritesTheOthers()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var kept = await AddInstrumentAsync(db, tenant, "AAPL", "USD", active: true);
        var mismatched = await AddInstrumentAsync(db, tenant, "MSFT", "USD", active: true);
        var written = await AddInstrumentAsync(db, tenant, "VTI", "USD", active: true);
        var previousFetched = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        db.InstrumentPrices.Add(Stored(kept.Id, 180m, new DateOnly(2026, 11, 1), previousFetched));
        db.InstrumentPrices.Add(Stored(mismatched.Id, 400m, new DateOnly(2026, 11, 1), previousFetched));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var source = new SelectiveSource(new DateOnly(2026, 11, 1));
        var job = Job(
            db,
            new FixedCatalog(
                new ActiveInstrument(kept.Id, "AAPL", "USD"),
                new ActiveInstrument(mismatched.Id, "MSFT", "USD"),
                new ActiveInstrument(written.Id, "VTI", "USD")),
            source,
            new FixedTime(new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero)));

        try
        {
            await job.Sync();

            source.Calls.Select(call => call.Symbol).OrderBy(symbol => symbol).ShouldBe(["AAPL", "MSFT", "VTI"]);
            var keptRow = await db.InstrumentPrices.AsNoTracking().SingleAsync(row => row.InstrumentId == kept.Id);
            keptRow.Price.ShouldBe(180m);
            keptRow.FetchedAt.ShouldBe(previousFetched);
            var mismatchedRow = await db.InstrumentPrices.AsNoTracking().SingleAsync(row => row.InstrumentId == mismatched.Id);
            mismatchedRow.Price.ShouldBe(400m);
            mismatchedRow.FetchedAt.ShouldBe(previousFetched);
            var writtenRow = await db.InstrumentPrices.AsNoTracking().SingleAsync(row => row.InstrumentId == written.Id);
            writtenRow.Price.ShouldBe(250m);
            writtenRow.AsOfDate.ShouldBe(new DateOnly(2026, 11, 1));
            writtenRow.Source.ShouldBe("EODHD");
        }
        finally
        {
            await CleanupAsync(db, tenant.Id, otherTenant.Id);
        }
    }

    [Test]
    public async Task Sync_SkipsTheSourceWhenEveryActiveInstrumentHasTodaysDate()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var instrument = await AddInstrumentAsync(db, tenant, "VTI", "USD", active: true);
        var today = new DateOnly(2026, 11, 2);
        var now = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
        var source = new RecordingSource(new DateOnly(2026, 11, 1), price: 250m, currency: "USD");
        var job = Job(db, new FixedCatalog(new ActiveInstrument(instrument.Id, "VTI", "USD")), source, new FixedTime(now));

        try
        {
            await job.Sync();
            source.Calls.Count.ShouldBeGreaterThan(0);

            source.Calls.Clear();
            db.InstrumentPrices.Add(Stored(instrument.Id, 250m, today, now));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await job.Sync();
            source.Calls.ShouldBeEmpty();
            (await db.InstrumentPrices.AsNoTracking().CountAsync(row =>
                row.InstrumentId == instrument.Id && row.AsOfDate == today)).ShouldBe(1);
        }
        finally
        {
            await CleanupAsync(db, tenant.Id, otherTenant.Id);
        }
    }

    [Test]
    public async Task ListActive_IncludesEveryTenantAndSkipsInactive()
    {
        var accessor = new TenantIdAccessor();
        await using var db = Open(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var activeA = await AddInstrumentAsync(db, tenantA, "VTI", "USD", active: true);
        var activeB = await AddInstrumentAsync(db, tenantB, "AIA", "NZD", active: true);
        var disabled = await AddInstrumentAsync(db, tenantA, "NZD-CASH", "NZD", active: false);
        accessor.Engage(tenantA.Id);

        try
        {
            var listed = await new ActiveInstrumentCatalog(db).ListActiveAsync();

            listed.ShouldContain(row => row.Id == activeA.Id && row.Symbol == "VTI" && row.QuoteCurrency == "USD");
            listed.ShouldContain(row => row.Id == activeB.Id && row.Symbol == "AIA" && row.QuoteCurrency == "NZD");
            listed.ShouldNotContain(row => row.Id == disabled.Id);
        }
        finally
        {
            await CleanupAsync(db, tenantA.Id, tenantB.Id);
        }
    }

    private static InstrumentPriceSyncJob Job(
        ApplicationDbContext db,
        IActiveInstrumentCatalog catalog,
        IInstrumentPriceSource source,
        TimeProvider time) =>
        new(catalog, source, new InstrumentPriceStore(db, time), time, TimeSpan.Zero, new SilentLogger());

    private static async Task<Instrument> AddInstrumentAsync(
        ApplicationDbContext db,
        Tenant tenant,
        string symbol,
        string quote,
        bool active)
    {
        var instrument = Instrument.Create(
            tenant.Id,
            symbol,
            symbol,
            Currency.Create(quote, quote, 2));
        if (!active)
        {
            instrument.Disable();
        }

        instrument.CreatedBy = "test";
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();
        return instrument;
    }

    private static InstrumentPrice Stored(int instrumentId, decimal price, DateOnly asOf, DateTimeOffset fetchedAt) =>
        new()
        {
            InstrumentId = instrumentId,
            Price = price,
            AsOfDate = asOf,
            Source = "EODHD",
            FetchedAt = fetchedAt
        };

    private static async Task CleanupAsync(ApplicationDbContext db, int tenantA, int tenantB)
    {
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM [InstrumentPrices]
            WHERE [InstrumentId] IN (SELECT [Id] FROM [Instruments] WHERE [TenantId] IN ({0}, {1}))
            """,
            tenantA,
            tenantB);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [Instruments] WHERE [TenantId] IN ({0}, {1})",
            tenantA,
            tenantB);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [Tenants] WHERE [Id] IN ({0}, {1})",
            tenantA,
            tenantB);
    }

    private sealed class FixedCatalog(params ActiveInstrument[] instruments) : IActiveInstrumentCatalog
    {
        public Task<IReadOnlyList<ActiveInstrument>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActiveInstrument>>(instruments);
    }

    private sealed class RecordingSource(DateOnly asOf, decimal price, string currency) : IInstrumentPriceSource
    {
        public List<(string Symbol, string QuoteCurrency)> Calls { get; } = [];

        public Task<SourcedInstrumentPrice?> GetLatestAsync(
            string symbol,
            string quoteCurrency,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((symbol, quoteCurrency));
            return Task.FromResult<SourcedInstrumentPrice?>(new SourcedInstrumentPrice(price, asOf, currency));
        }
    }

    private sealed class SelectiveSource(DateOnly asOf) : IInstrumentPriceSource
    {
        public List<(string Symbol, string QuoteCurrency)> Calls { get; } = [];

        public Task<SourcedInstrumentPrice?> GetLatestAsync(
            string symbol,
            string quoteCurrency,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((symbol, quoteCurrency));
            if (symbol == "AAPL")
            {
                return Task.FromResult<SourcedInstrumentPrice?>(null);
            }

            if (symbol == "MSFT")
            {
                return Task.FromResult<SourcedInstrumentPrice?>(new SourcedInstrumentPrice(400m, asOf, "EUR"));
            }

            return Task.FromResult<SourcedInstrumentPrice?>(new SourcedInstrumentPrice(250m, asOf, quoteCurrency));
        }
    }

    private sealed class ThrowingSource : IInstrumentPriceSource
    {
        public Task<SourcedInstrumentPrice?> GetLatestAsync(
            string symbol,
            string quoteCurrency,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Twelve Data is down.");
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SilentLogger : ILogger<InstrumentPriceSyncJob>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private static ApplicationDbContext Open(TenantIdAccessor? accessor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return accessor is null
            ? new ApplicationDbContext(options)
            : new ApplicationDbContext(options, accessor);
    }
}
