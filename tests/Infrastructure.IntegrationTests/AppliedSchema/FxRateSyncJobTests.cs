using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fx;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class FxRateSyncJobTests
{
    [Test]
    public async Task Sync_SourceFailure_LeavesPreviousRows()
    {
        await using var db = Open();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [FxRates]");
        var fetchedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        db.FxRates.Add(new FxRate
        {
            BaseCurrency = "USD",
            QuoteCurrency = "NZD",
            Rate = 1.50000000m,
            AsOfDate = new DateOnly(2026, 11, 1),
            Source = "Frankfurter",
            FetchedAt = fetchedAt
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var job = new FxRateSyncJob(
            new FixedCatalog("USD", "NZD"),
            new ThrowingSource(),
            new FxRateStore(db, TimeProvider.System),
            TimeProvider.System,
            new SilentLogger());

        try
        {
            await job.Sync();

            var row = await db.FxRates.AsNoTracking().SingleAsync();
            row.Rate.ShouldBe(1.50000000m);
            row.FetchedAt.ShouldBe(fetchedAt);
            row.AsOfDate.ShouldBe(new DateOnly(2026, 11, 1));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [FxRates]");
        }
    }

    private sealed class ThrowingSource : IFxRateSource
    {
        public Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
            string baseCurrency,
            IReadOnlyList<string> quotes,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Frankfurter is down.");
    }

    private sealed class FixedCatalog(params string[] codes) : ICurrencyCatalog
    {
        public Currency? TryGet(string code) =>
            codes.Contains(code, StringComparer.OrdinalIgnoreCase)
                ? Currency.Create(code, code, 2)
                : null;

        public IReadOnlyList<Currency> List(bool enabledOnly = false) =>
            codes.Select(code => Currency.Create(code, code, 2)).ToList();

        public bool IsActive(string code) => codes.Contains(code, StringComparer.OrdinalIgnoreCase);

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SilentLogger : ILogger<FxRateSyncJob>
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

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
