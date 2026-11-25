using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

[Parallelizable]
public class InstrumentPriceUpsertTests
{
    [Test]
    public async Task Upsert_SameKey_UpdatesPriceAndFetchedAt_AndANewDateInserts()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var usd = Currency.Create("USD", "United States Dollar", 2);
        var instrument = Instrument.Create(tenant.Id, "VTI", "Vanguard Total Stock Market ETF", usd);
        var other = Instrument.Create(tenant.Id, "AAPL", "Apple Inc.", usd);
        instrument.CreatedBy = "test";
        other.CreatedBy = "test";
        db.Instruments.AddRange(instrument, other);
        await db.SaveChangesAsync();

        var store = new InstrumentPriceStore(db, TimeProvider.System);
        var firstFetched = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);
        var thirdFetched = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero);
        var asOf = new DateOnly(2026, 11, 2);

        try
        {
            await store.UpsertAsync(
                [new InstrumentPriceQuote(instrument.Id, 250m, asOf)],
                firstFetched);
            await store.UpsertAsync(
                [
                    new InstrumentPriceQuote(instrument.Id, 251m, asOf),
                    new InstrumentPriceQuote(other.Id, 180m, asOf)
                ],
                secondFetched);

            var sameDay = await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == instrument.Id && row.AsOfDate == asOf)
                .ToListAsync();
            var updated = sameDay.ShouldHaveSingleItem();
            updated.Price.ShouldBe(251m);
            updated.FetchedAt.ShouldBe(secondFetched);
            updated.Source.ShouldBe("EODHD");
            (await db.InstrumentPrices.AsNoTracking().SingleAsync(row => row.InstrumentId == other.Id))
                .Price.ShouldBe(180m);

            await store.UpsertAsync(
                [new InstrumentPriceQuote(instrument.Id, 260m, new DateOnly(2026, 11, 3))],
                thirdFetched);

            var dates = await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == instrument.Id)
                .OrderBy(row => row.AsOfDate)
                .ToListAsync();
            dates.Count.ShouldBe(2);
            dates[0].Price.ShouldBe(251m);
            dates[0].FetchedAt.ShouldBe(secondFetched);
            dates[1].Price.ShouldBe(260m);
            dates[1].FetchedAt.ShouldBe(thirdFetched);
            dates[1].AsOfDate.ShouldBe(new DateOnly(2026, 11, 3));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [InstrumentPrices] WHERE [InstrumentId] IN ({0}, {1})",
                instrument.Id,
                other.Id);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [Instruments] WHERE [Id] IN ({0}, {1})",
                instrument.Id,
                other.Id);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [Tenants] WHERE [Id] IN ({0}, {1})",
                tenant.Id,
                otherTenant.Id);
        }
    }

    /// <summary>Market data history R4–R6: a final row is never updated; a missing past date is inserted; today's
    /// and yesterday's rows are updated. "Today" is the UTC date of the write's <c>fetchedAt</c>.</summary>
    [Test]
    public async Task Upsert_FinalRowKeepsItsValues_MissingPastDateInserts_TodayAndYesterdayUpdate()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var usd = Currency.Create("USD", "United States Dollar", 2);
        var instrument = Instrument.Create(tenant.Id, "AAPL", "Apple Inc.", usd);
        instrument.CreatedBy = "test";
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();

        var store = new InstrumentPriceStore(db, TimeProvider.System);
        var firstFetched = new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
        var secondFetched = new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero);
        DateOnly Day(int day) => new(2026, 11, day);

        try
        {
            await store.UpsertAsync(
                [
                    new InstrumentPriceQuote(instrument.Id, 100m, Day(10)),
                    new InstrumentPriceQuote(instrument.Id, 100m, Day(18)),
                    new InstrumentPriceQuote(instrument.Id, 100m, Day(19)),
                    new InstrumentPriceQuote(instrument.Id, 100m, Day(20))
                ],
                firstFetched);
            await store.UpsertAsync(
                [
                    new InstrumentPriceQuote(instrument.Id, 50m, Day(5)),
                    new InstrumentPriceQuote(instrument.Id, 200m, Day(10)),
                    new InstrumentPriceQuote(instrument.Id, 200m, Day(18)),
                    new InstrumentPriceQuote(instrument.Id, 200m, Day(19)),
                    new InstrumentPriceQuote(instrument.Id, 200m, Day(20))
                ],
                secondFetched,
                "Simulator");

            var rows = (await db.InstrumentPrices.AsNoTracking()
                    .Where(row => row.InstrumentId == instrument.Id)
                    .ToListAsync())
                .ToDictionary(row => row.AsOfDate);
            rows.Count.ShouldBe(5);

            rows[Day(5)].Price.ShouldBe(50m);
            rows[Day(5)].Source.ShouldBe("Simulator");
            rows[Day(5)].FetchedAt.ShouldBe(secondFetched);

            foreach (var final in new[] { Day(10), Day(18) })
            {
                rows[final].Price.ShouldBe(100m);
                rows[final].Source.ShouldBe("EODHD");
                rows[final].FetchedAt.ShouldBe(firstFetched);
            }

            foreach (var open in new[] { Day(19), Day(20) })
            {
                rows[open].Price.ShouldBe(200m);
                rows[open].Source.ShouldBe("Simulator");
                rows[open].FetchedAt.ShouldBe(secondFetched);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [InstrumentPrices] WHERE [InstrumentId] = {0}", instrument.Id);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [Instruments] WHERE [Id] = {0}", instrument.Id);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [Tenants] WHERE [Id] IN ({0}, {1})", tenant.Id, otherTenant.Id);
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
