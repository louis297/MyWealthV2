using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

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
            updated.Source.ShouldBe("TwelveData");
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
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [InstrumentPrices] WHERE [InstrumentId] IN ({0}, {1})",
                instrument.Id,
                other.Id);
            db.Instruments.RemoveRange(instrument, other);
            db.Tenants.RemoveRange(tenant, otherTenant);
            await db.SaveChangesAsync();
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
