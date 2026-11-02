using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Fx;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

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

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
