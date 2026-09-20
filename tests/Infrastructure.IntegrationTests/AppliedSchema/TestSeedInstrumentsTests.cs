using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TestSeedInstrumentsTests
{
    [Test]
    public async Task Seed_IsIdempotentAndSeedsMockPrices()
    {
        await using var db = OpenDb();
        await EnsureDemoTenantAsync(db);
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var first = await LoadSeededAsync(db);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var second = await LoadSeededAsync(db);

        first.Select(row => row.Symbol).ShouldBe(["AIA", "NZD-CASH", "VTI"]);
        second.Select(row => row.PublicId).ShouldBe(first.Select(row => row.PublicId));

        var vti = first.Single(row => row.Symbol == "VTI");
        vti.Name.ShouldBe("Vanguard Total Stock Market ETF");
        vti.QuoteCurrency.ShouldBe("USD");
        vti.IsActive.ShouldBeTrue();

        var aia = first.Single(row => row.Symbol == "AIA");
        aia.Name.ShouldBe("Auckland International Airport");
        aia.QuoteCurrency.ShouldBe("NZD");
        aia.IsActive.ShouldBeTrue();

        var cash = first.Single(row => row.Symbol == "NZD-CASH");
        cash.Name.ShouldBe("Demo disabled cash proxy");
        cash.QuoteCurrency.ShouldBe("NZD");
        cash.IsActive.ShouldBeFalse();

        prices.TryGetPrice(vti.Id).ShouldNotBeNull();
        prices.TryGetPrice(vti.Id)!.Currency.ShouldBe("USD");
        prices.TryGetPrice(aia.Id).ShouldNotBeNull();
        prices.TryGetPrice(aia.Id)!.Currency.ShouldBe("NZD");
        prices.TryGetPrice(cash.Id).ShouldBeNull();
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
