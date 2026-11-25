using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Fx;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

[Parallelizable]
public class FxRateUpsertTests
{
    [Test]
    public async Task Upsert_SameKey_UpdatesRateAndFetchedAt()
    {
        await using var db = Open();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [FxRates] WHERE [BaseCurrency] = 'USD' AND [QuoteCurrency] = 'NZD'");
        var store = new FxRateStore(db, TimeProvider.System);
        var firstFetched = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);

        try
        {
            await store.UpsertAsync(
                [new FxRateQuote("usd", "nzd", 1.50000000m, new DateOnly(2026, 11, 2))],
                firstFetched);
            await store.UpsertAsync(
                [new FxRateQuote("USD", "NZD", 1.60000000m, new DateOnly(2026, 11, 2))],
                secondFetched);

            var rows = await db.FxRates.AsNoTracking()
                .Where(row => row.BaseCurrency == "USD" && row.QuoteCurrency == "NZD")
                .ToListAsync();
            var row = rows.ShouldHaveSingleItem();
            row.Rate.ShouldBe(1.60000000m);
            row.FetchedAt.ShouldBe(secondFetched);
            row.Source.ShouldBe("Frankfurter");
            row.AsOfDate.ShouldBe(new DateOnly(2026, 11, 2));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [FxRates] WHERE [BaseCurrency] = 'USD' AND [QuoteCurrency] = 'NZD'");
        }
    }

    /// <summary>Market data history R4–R6: a final row keeps its rate, source and fetch time; a missing past date is
    /// inserted; today's and yesterday's rows are updated. "Today" is the UTC date of the write's <c>fetchedAt</c>.</summary>
    [Test]
    public async Task Upsert_FinalRowKeepsItsValues_MissingPastDateInserts_TodayAndYesterdayUpdate()
    {
        await using var db = Open();
        await DeleteEurJpyAsync(db);
        var store = new FxRateStore(db, TimeProvider.System);
        var firstFetched = new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero);
        DateOnly Day(int day) => new(2026, 11, day);

        try
        {
            await store.UpsertAsync(
                [
                    new FxRateQuote("EUR", "JPY", 160m, Day(10)),
                    new FxRateQuote("EUR", "JPY", 160m, Day(18)),
                    new FxRateQuote("EUR", "JPY", 160m, Day(19)),
                    new FxRateQuote("EUR", "JPY", 160m, Day(20))
                ],
                firstFetched);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE [FxRates] SET [Source] = 'Earlier' WHERE [BaseCurrency] = 'EUR' AND [QuoteCurrency] = 'JPY'");
            db.ChangeTracker.Clear();
            await store.UpsertAsync(
                [
                    new FxRateQuote("eur", "jpy", 155m, Day(5)),
                    new FxRateQuote("EUR", "JPY", 170m, Day(10)),
                    new FxRateQuote("EUR", "JPY", 170m, Day(18)),
                    new FxRateQuote("EUR", "JPY", 170m, Day(19)),
                    new FxRateQuote("EUR", "JPY", 170m, Day(20))
                ],
                secondFetched);

            var rows = (await db.FxRates.AsNoTracking()
                    .Where(row => row.BaseCurrency == "EUR" && row.QuoteCurrency == "JPY")
                    .ToListAsync())
                .ToDictionary(row => row.AsOfDate);
            rows.Count.ShouldBe(5);

            rows[Day(5)].Rate.ShouldBe(155m);
            rows[Day(5)].Source.ShouldBe("Frankfurter");
            rows[Day(5)].FetchedAt.ShouldBe(secondFetched);

            foreach (var final in new[] { Day(10), Day(18) })
            {
                rows[final].Rate.ShouldBe(160m);
                rows[final].Source.ShouldBe("Earlier");
                rows[final].FetchedAt.ShouldBe(firstFetched);
            }

            foreach (var open in new[] { Day(19), Day(20) })
            {
                rows[open].Rate.ShouldBe(170m);
                rows[open].Source.ShouldBe("Frankfurter");
                rows[open].FetchedAt.ShouldBe(secondFetched);
            }
        }
        finally
        {
            await DeleteEurJpyAsync(db);
        }
    }

    private static Task DeleteEurJpyAsync(ApplicationDbContext db) =>
        db.Database.ExecuteSqlRawAsync("DELETE FROM [FxRates] WHERE [BaseCurrency] = 'EUR' AND [QuoteCurrency] = 'JPY'");

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
