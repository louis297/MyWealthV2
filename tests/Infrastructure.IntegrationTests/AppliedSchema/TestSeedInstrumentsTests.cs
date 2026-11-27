using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

[Parallelizable]
public class TestSeedInstrumentsTests
{
    /// <summary>Valuation run §9.1: the weekdays from 2026-01-02 to 2026-03-31, one history row each.</summary>
    private static readonly int HistoryWeekdays = Enumerable.Range(0, 89)
        .Select(offset => new DateOnly(2026, 1, 2).AddDays(offset))
        .Count(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);

    private static readonly (string Symbol, string Name, string Quote, bool Active, decimal? Price)[] Catalog =
    [
        ("6758", "Sony Group", "JPY", true, 13000m),
        ("7203", "Toyota Motor", "JPY", true, 3000m),
        ("9984", "SoftBank Group", "JPY", true, 8000m),
        ("AAPL", "Apple Inc.", "USD", true, 180m),
        ("ACME.SIM", "Acme Simulated Holdings", "USD", true, 100m),
        ("AIA", "Auckland International Airport", "NZD", true, 8.50m),
        ("AIR", "Airbus SE", "EUR", true, 150m),
        ("BHP", "BHP Group", "AUD", true, 45m),
        ("CBA", "Commonwealth Bank of Australia", "AUD", true, 130m),
        ("FPH", "Fisher & Paykel Healthcare", "NZD", true, 30m),
        ("HSBA", "HSBC Holdings", "GBP", true, 7m),
        ("KIWI.SIM", "Kiwi Simulated Fund", "NZD", true, 5m),
        ("MEL", "Meridian Energy", "NZD", true, 5.50m),
        ("MSFT", "Microsoft Corporation", "USD", true, 400m),
        ("NZD-CASH", "Demo disabled cash proxy", "NZD", false, null),
        ("OR", "L'Oréal", "EUR", true, 400m),
        ("SAP", "SAP SE", "EUR", true, 180m),
        ("SHEL", "Shell plc", "GBP", true, 28m),
        ("ULVR", "Unilever", "GBP", true, 45m),
        ("VTI", "Vanguard Total Stock Market ETF", "USD", true, 250m),
        ("WOW", "Woolworths Group", "AUD", true, 35m)
    ];

    [Test]
    public async Task Seed_IsIdempotentAndSeedsPrices()
    {
        await using var db = OpenDb();
        await EnsureDemoTenantAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        var first = await LoadSeededAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        var second = await LoadSeededAsync(db);

        first.Select(row => row.Symbol).ShouldBe(Catalog.Select(row => row.Symbol));
        second.Select(row => row.PublicId).ShouldBe(first.Select(row => row.PublicId));

        foreach (var expected in Catalog)
        {
            var instrument = first.Single(row => row.Symbol == expected.Symbol);
            instrument.Name.ShouldBe(expected.Name);
            instrument.QuoteCurrency.ShouldBe(expected.Quote);
            instrument.IsActive.ShouldBe(expected.Active);
        }

        var ids = first.Select(instrument => instrument.Id).ToList();
        var stored = await db.InstrumentPrices.AsNoTracking()
            .Where(row => ids.Contains(row.InstrumentId))
            .ToListAsync();
        var asOf = new DateOnly(2026, 1, 1);
        foreach (var expected in Catalog)
        {
            var instrument = first.Single(row => row.Symbol == expected.Symbol);
            var price = stored.SingleOrDefault(row => row.InstrumentId == instrument.Id && row.AsOfDate == asOf);
            if (expected.Price is null)
            {
                price.ShouldBeNull();
                continue;
            }

            price.ShouldNotBeNull();
            price.Price.ShouldBe(expected.Price.Value);
            // Simulated instruments §9.1: a .SIM row is the simulator's price.
            price.Source.ShouldBe(expected.Symbol.EndsWith(".SIM", StringComparison.Ordinal) ? "Simulator" : "EODHD");
        }

        (await db.InstrumentPrices.AsNoTracking().CountAsync(row => ids.Contains(row.InstrumentId)))
            .ShouldBe(Catalog.Count(row => row.Price is not null)
                      + Catalog.Count(row => row.Price is not null && !row.Symbol.EndsWith(".SIM", StringComparison.Ordinal)) * HistoryWeekdays);
    }

    // Instrument-tax-attributes R25: the demo catalog as §5.1, ACME.SIM and KIWI.SIM as fixtures, NZD-CASH not classified.
    [Test]
    public async Task Seed_ClassifiesTheCatalogAndTheSimulatedInstruments()
    {
        await using var db = OpenDb();
        await EnsureDemoTenantAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        var first = await LoadTaxAttributesAsync(db);
        var firstRows = await RowIdsAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        (await LoadTaxAttributesAsync(db)).ShouldBe(first);
        (await RowIdsAsync(db)).ShouldBe(firstRows);
        first.Count.ShouldBe(Catalog.Length);
        first["VTI"].ShouldBe("US|2027 Attributing ");
        first["AIA"].ShouldBe("NZ|2027 NotForeign ");
        first["BHP"].ShouldBe("AU|2027 ExemptAsxListed Assumed from tax year 2026: on 2026-11-27 Inland Revenue's FIF Australian listed share exemption tool showed the exemption for 1 April 2025 to 31 March 2026 only.");
        first["AIR"].ShouldBe("NL|2027 Attributing ");
        first["ACME.SIM"].ShouldBe("US|2027 Attributing ");
        first["KIWI.SIM"].ShouldBe("NZ|2027 NotForeign ");
        first["NZD-CASH"].ShouldBe("|");
        first.Count(pair => pair.Value != "|").ShouldBe(Catalog.Length - 1);
    }

    private static async Task<List<int>> RowIdsAsync(ApplicationDbContext db)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        return await db.InstrumentFifTreatments.AsNoTracking()
            .Where(row => row.TenantId == tenant.Id)
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync();
    }

    private static async Task<Dictionary<string, string>> LoadTaxAttributesAsync(ApplicationDbContext db)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        var instruments = await db.Instruments.AsNoTracking().Include(row => row.FifTreatments)
            .Where(row => row.TenantId == tenant.Id)
            .ToListAsync();
        return instruments.ToDictionary(
            row => row.Symbol,
            row => $"{row.IssuerCountry}|" + string.Join(",", row.FifTreatments
                .OrderBy(fif => fif.TaxYear)
                .Select(fif => $"{fif.TaxYear} {fif.Treatment} {fif.Note}")));
    }

    private static async Task EnsureDemoTenantAsync(ApplicationDbContext db)
    {
        if (await db.Tenants.AnyAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode))
        {
            return;
        }

        db.Tenants.Add(Tenant.Create(
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode,
            Currency.Create("NZD", "New Zealand Dollar", 2)));
        await db.SaveChangesAsync();
    }

    private static async Task<List<Instrument>> LoadSeededAsync(ApplicationDbContext db)
    {
        var tenant = await db.Tenants.SingleAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        return await db.Instruments.AsNoTracking()
            .Where(row => row.TenantId == tenant.Id)
            .OrderBy(row => row.Symbol)
            .ToListAsync();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
