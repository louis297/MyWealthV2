using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

// fixes/utc-instants.md B4: TryGetPrice reads the UTC date of asOf, not the date in its own offset.
public class InstrumentPriceReadTests
{
    [Test]
    public async Task TryGetPrice_ReadsTheUtcDateOfAsOf()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var instrument = Instrument.Create(tenant.Id, "VTI", "Vanguard Total Stock Market ETF", Currency.Create("USD", "United States Dollar", 2));
        instrument.CreatedBy = "test";
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();

        try
        {
            var store = new InstrumentPriceStore(db, TimeProvider.System);
            var fetched = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
            await store.UpsertAsync(
                [
                    new Application.MarketData.InstrumentPriceQuote(instrument.Id, 250m, new DateOnly(2026, 10, 10)),
                    new Application.MarketData.InstrumentPriceQuote(instrument.Id, 260m, new DateOnly(2026, 10, 11))
                ],
                fetched);

            store.TryGetPrice(instrument.Id, new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(13)))!
                .Amount.ShouldBe(250m);
            store.TryGetPrice(instrument.Id, new DateTimeOffset(2026, 10, 11, 0, 30, 0, TimeSpan.Zero))!
                .Amount.ShouldBe(260m);
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
