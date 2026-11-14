using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class FxRateReadTests
{
    [Test]
    public async Task GetRate_ReadsTheLatestStoredPairOnOrBeforeTheBookedDate()
    {
        await using var db = Open();
        await DeletePairAsync(db);
        db.FxRates.AddRange(
            Rate("USD", "NZD", 1.50000000m, new DateOnly(2026, 1, 1)),
            Rate("USD", "NZD", 1.60000000m, new DateOnly(2026, 10, 11)));
        await db.SaveChangesAsync();

        try
        {
            var fx = new FxRateStore(db, TimeProvider.System);
            var booked = new DateTimeOffset(2026, 10, 11, 0, 30, 0, TimeSpan.Zero);
            (await fx.GetRateAsync("usd", "nzd", booked)).ShouldBe(1.60000000m);
            (await fx.GetRateAsync("NZD", "NZD", booked)).ShouldBe(1m);
        }
        finally
        {
            await DeletePairAsync(db);
        }
    }

    // fixes/utc-instants.md B4: the requested date is the UTC date of asOf, not the date in its own offset.
    [Test]
    public async Task GetRate_AndGetRateDetail_ReadTheUtcDateOfAsOf()
    {
        await using var db = Open();
        await DeletePairAsync(db);
        db.FxRates.AddRange(
            Rate("USD", "NZD", 1.50000000m, new DateOnly(2026, 10, 10)),
            Rate("USD", "NZD", 1.60000000m, new DateOnly(2026, 10, 11)));
        await db.SaveChangesAsync();

        try
        {
            var fx = new FxRateStore(db, TimeProvider.System);
            var nzMorning = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(13));
            var utcSameDay = new DateTimeOffset(2026, 10, 11, 0, 30, 0, TimeSpan.Zero);

            (await fx.GetRateAsync("USD", "NZD", nzMorning)).ShouldBe(1.50000000m);
            (await fx.GetRateAsync("USD", "NZD", utcSameDay)).ShouldBe(1.60000000m);
            (await fx.GetRateDetailAsync("USD", "NZD", nzMorning)).AsOfDate.ShouldBe(new DateOnly(2026, 10, 10));
            (await fx.GetRateDetailAsync("USD", "NZD", utcSameDay)).AsOfDate.ShouldBe(new DateOnly(2026, 10, 11));
        }
        finally
        {
            await DeletePairAsync(db);
        }
    }

    [Test]
    public async Task TestSeed_InsertsTheLockedPairsOnce()
    {
        await using var db = Open();
        await DeleteAllAsync(db);
        try
        {
            await TestSeed.SeedAsync(db, CancellationToken.None);
            await TestSeed.SeedAsync(db, CancellationToken.None);

            var rows = await db.FxRates.AsNoTracking().ToListAsync();
            rows.Count.ShouldBe(10);
            rows.ShouldAllBe(row => row.AsOfDate == new DateOnly(2026, 1, 1));
            rows.ShouldAllBe(row => row.Source == "Frankfurter");
            Pair(rows, "USD", "NZD").ShouldBe(1.50000000m);
            Pair(rows, "USD", "AUD").ShouldBe(1.40000000m);
            Pair(rows, "USD", "EUR").ShouldBe(0.90000000m);
            Pair(rows, "USD", "GBP").ShouldBe(0.80000000m);
            Pair(rows, "USD", "JPY").ShouldBe(150.00000000m);
            Pair(rows, "NZD", "USD").ShouldBe(0.66666667m);
            Pair(rows, "AUD", "USD").ShouldBe(0.71428571m);
            Pair(rows, "EUR", "USD").ShouldBe(1.11111111m);
            Pair(rows, "GBP", "USD").ShouldBe(1.25000000m);
            Pair(rows, "JPY", "USD").ShouldBe(0.00666667m);
            rows.ShouldNotContain(row =>
                row.BaseCurrency.Trim() == "EUR" && row.QuoteCurrency.Trim() == "JPY");
        }
        finally
        {
            await DeleteAllAsync(db);
        }
    }

    private static FxRate Rate(string baseCurrency, string quoteCurrency, decimal rate, DateOnly asOf) =>
        new()
        {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            Rate = rate,
            AsOfDate = asOf,
            Source = "Frankfurter",
            FetchedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

    private static decimal Pair(IReadOnlyList<FxRate> rows, string baseCurrency, string quoteCurrency) =>
        rows.Single(row => row.BaseCurrency.Trim() == baseCurrency && row.QuoteCurrency.Trim() == quoteCurrency).Rate;

    private static async Task DeletePairAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [FxRates] WHERE [BaseCurrency] = 'USD' AND [QuoteCurrency] = 'NZD'");
    }

    private static async Task DeleteAllAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [FxRates]");
    }

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
