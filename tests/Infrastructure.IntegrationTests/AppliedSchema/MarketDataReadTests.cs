using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.MarketData;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Market data history R17, R18: the price with its date, and the lists of stored prices and rates.</summary>
[Parallelizable]
public class MarketDataReadTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 26, 12, 0, 4, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterFetched = new(2026, 11, 26, 13, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TryGetPriceDetail_ReadsTheLatestRowOnOrBeforeTheDate_WithItsDateAndSource()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var instrument = await AddInstrumentAsync(db, tenant);
        db.InstrumentPrices.AddRange(
            Price(instrument.Id, 2025, 12, 31, 271.86m, "EODHD"),
            Price(instrument.Id, 2026, 1, 2, 271.01m, "EODHD"),
            Price(instrument.Id, 2026, 1, 5, 267.26m, "Simulator"));
        await db.SaveChangesAsync();
        var store = new InstrumentPriceStore(db, TimeProvider.System);

        try
        {
            // §1.1: the weekend and New Year's Day answer the trading day before them.
            var weekend = store.TryGetPriceDetail(instrument.Id, new DateTimeOffset(2026, 1, 4, 23, 0, 0, TimeSpan.Zero));
            weekend.ShouldNotBeNull();
            weekend.Price.Amount.ShouldBe(271.01m);
            weekend.Price.Currency.ShouldBe("USD");
            weekend.AsOfDate.ShouldBe(new DateOnly(2026, 1, 2));
            weekend.Source.ShouldBe("EODHD");
            store.TryGetPrice(instrument.Id, new DateTimeOffset(2026, 1, 4, 23, 0, 0, TimeSpan.Zero))!.Amount.ShouldBe(271.01m);

            var holiday = store.TryGetPriceDetail(instrument.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            holiday.ShouldNotBeNull();
            holiday.AsOfDate.ShouldBe(new DateOnly(2025, 12, 31));

            var latest = store.TryGetPriceDetail(instrument.Id, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
            latest.ShouldNotBeNull();
            latest.Source.ShouldBe("Simulator");

            store.TryGetPriceDetail(instrument.Id, new DateTimeOffset(2025, 12, 30, 0, 0, 0, TimeSpan.Zero)).ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(db, tenant.Id, otherTenant.Id);
        }
    }

    [Test]
    public async Task ListPricesAsync_ReadsTheRange_NewestFirst_WithPaging()
    {
        await using var db = Open();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var instrument = await AddInstrumentAsync(db, tenant);
        var other = await AddInstrumentAsync(db, tenant, "MSFT");
        db.InstrumentPrices.AddRange(
            Price(instrument.Id, 2025, 12, 29, 273.76m, "EODHD"),
            Price(instrument.Id, 2025, 12, 30, 273.08m, "EODHD"),
            Price(instrument.Id, 2025, 12, 31, 271.86m, "EODHD"),
            Price(instrument.Id, 2026, 1, 2, 271.01m, "EODHD"),
            Price(instrument.Id, 2026, 1, 5, 267.26m, "EODHD", LaterFetched),
            Price(other.Id, 2026, 1, 2, 1m, "EODHD"));
        await db.SaveChangesAsync();
        var store = new InstrumentPriceStore(db, TimeProvider.System);

        try
        {
            var all = await store.ListPricesAsync(instrument.Id, null, null, 1, 20);
            all.TotalCount.ShouldBe(5);
            all.Page.ShouldBe(1);
            all.PageSize.ShouldBe(20);
            all.Items.Select(row => row.AsOfDate.ToString("yyyy-MM-dd")).ShouldBe(
                ["2026-01-05", "2026-01-02", "2025-12-31", "2025-12-30", "2025-12-29"]);
            all.Items[0].ShouldBe(new MyWealthV2.Application.Common.Interfaces.StoredInstrumentPrice(
                new DateOnly(2026, 1, 5), 267.26m, "EODHD", LaterFetched));

            var range = await store.ListPricesAsync(instrument.Id, new DateOnly(2025, 12, 30), new DateOnly(2026, 1, 2), 1, 20);
            range.TotalCount.ShouldBe(3);
            range.Items.Select(row => row.Price).ShouldBe([271.01m, 271.86m, 273.08m]);

            var fromOnly = await store.ListPricesAsync(instrument.Id, new DateOnly(2026, 1, 2), null, 1, 20);
            fromOnly.Items.Select(row => row.AsOfDate).ShouldBe([new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 2)]);

            // R22: to with pageSize 1 is the row a read as of that date chooses.
            var asOf = await store.ListPricesAsync(instrument.Id, null, new DateOnly(2026, 1, 4), 1, 1);
            asOf.TotalCount.ShouldBe(4);
            asOf.Items.ShouldHaveSingleItem().AsOfDate.ShouldBe(new DateOnly(2026, 1, 2));

            var second = await store.ListPricesAsync(instrument.Id, null, null, 2, 2);
            second.TotalCount.ShouldBe(5);
            second.Items.Select(row => row.AsOfDate).ShouldBe([new DateOnly(2025, 12, 31), new DateOnly(2025, 12, 30)]);

            var none = await store.ListPricesAsync(instrument.Id, null, new DateOnly(2025, 12, 28), 1, 20);
            none.TotalCount.ShouldBe(0);
            none.Items.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(db, tenant.Id, otherTenant.Id);
        }
    }

    [Test]
    public async Task ListRatesAsync_ReadsTheDirectedPairOnly_InTheRange_NewestFirst_WithPaging()
    {
        await using var db = Open();
        await DeleteAudJpyAsync(db);
        db.FxRates.AddRange(
            Rate("AUD", "JPY", 2026, 1, 1, 100.1m),
            Rate("AUD", "JPY", 2026, 1, 2, 100.2m),
            Rate("AUD", "JPY", 2026, 1, 3, 100.3m),
            Rate("AUD", "JPY", 2026, 1, 4, 100.4m),
            Rate("JPY", "AUD", 2026, 1, 5, 0.01m),
            Rate("JPY", "AUD", 2026, 1, 4, 0.01m));
        await db.SaveChangesAsync();
        var store = new FxRateStore(db, TimeProvider.System);

        try
        {
            var all = await store.ListRatesAsync("aud", "jpy", null, null, 1, 20);
            all.TotalCount.ShouldBe(4);
            all.Items.Select(row => row.Rate).ShouldBe([100.4m, 100.3m, 100.2m, 100.1m]);
            all.Items[0].ShouldBe(new MyWealthV2.Application.Common.Interfaces.StoredFxRate(
                new DateOnly(2026, 1, 4), "AUD", "JPY", 100.4m, "Frankfurter", Fetched));

            var range = await store.ListRatesAsync("AUD", "JPY", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 3), 1, 20);
            range.TotalCount.ShouldBe(2);
            range.Items.Select(row => row.AsOfDate).ShouldBe([new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 2)]);

            var page = await store.ListRatesAsync("AUD", "JPY", null, null, 2, 3);
            page.TotalCount.ShouldBe(4);
            page.Items.ShouldHaveSingleItem().AsOfDate.ShouldBe(new DateOnly(2026, 1, 1));

            var inverse = await store.ListRatesAsync("JPY", "AUD", new DateOnly(2026, 1, 5), null, 1, 20);
            inverse.Items.ShouldHaveSingleItem().Rate.ShouldBe(0.01m);

            var itself = await store.ListRatesAsync("AUD", "AUD", null, null, 1, 20);
            itself.TotalCount.ShouldBe(0);
            itself.Items.ShouldBeEmpty();
        }
        finally
        {
            await DeleteAudJpyAsync(db);
        }
    }

    private static InstrumentPrice Price(int instrumentId, int year, int month, int day, decimal price, string source, DateTimeOffset? fetchedAt = null) =>
        new()
        {
            InstrumentId = instrumentId,
            AsOfDate = new DateOnly(year, month, day),
            Price = price,
            Source = source,
            FetchedAt = fetchedAt ?? Fetched
        };

    private static FxRate Rate(string baseCurrency, string quoteCurrency, int year, int month, int day, decimal rate) =>
        new()
        {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            AsOfDate = new DateOnly(year, month, day),
            Rate = rate,
            Source = "Frankfurter",
            FetchedAt = Fetched
        };

    private static async Task<Instrument> AddInstrumentAsync(ApplicationDbContext db, Tenant tenant, string symbol = "AAPL")
    {
        var instrument = Instrument.Create(tenant.Id, symbol, symbol, Currency.Create("USD", "United States Dollar", 2));
        instrument.CreatedBy = "test";
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();
        return instrument;
    }

    private static Task DeleteAudJpyAsync(ApplicationDbContext db) =>
        db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [FxRates] WHERE ([BaseCurrency] = 'AUD' AND [QuoteCurrency] = 'JPY') OR ([BaseCurrency] = 'JPY' AND [QuoteCurrency] = 'AUD')");

    private static async Task CleanupAsync(ApplicationDbContext db, int tenantA, int tenantB)
    {
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [InstrumentPrices] WHERE [InstrumentId] IN (SELECT [Id] FROM [Instruments] WHERE [TenantId] IN ({0}, {1}))",
            tenantA,
            tenantB);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Instruments] WHERE [TenantId] IN ({0}, {1})", tenantA, tenantB);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Tenants] WHERE [Id] IN ({0}, {1})", tenantA, tenantB);
    }

    private static ApplicationDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
