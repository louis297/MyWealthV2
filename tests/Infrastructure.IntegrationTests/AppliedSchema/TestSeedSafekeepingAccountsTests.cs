using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>In-kind transfers §9.1: TestSeed gives each seeded firm an active USD and NZD safekeeping account on the
/// simulator connection.</summary>
public class TestSeedSafekeepingAccountsTests
{
    [TestCase(DevelopmentIdentitySeeder.DemoTenantCode, DevelopmentIdentitySeeder.DemoTenantName)]
    [TestCase(DevelopmentIdentitySeeder.SecondTenantCode, DevelopmentIdentitySeeder.SecondTenantName)]
    public async Task SeedTwice_GivesTheFirmOneActiveUsdAndOneActiveNzdSafekeepingAccount(string code, string name)
    {
        await using var db = OpenDb();
        var tenant = await EnsureTenantAsync(db, code, name);

        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var rows = await db.SafekeepingAccounts.AsNoTracking()
            .Where(row => row.TenantId == tenant.Id)
            .OrderBy(row => row.Currency)
            .ToListAsync();
        rows.Select(row => row.Currency).ShouldBe(["NZD", "USD"]);
        rows.ShouldAllBe(row => row.IsActive);
        rows.ShouldAllBe(row => row.CustodianName == "Simulator Custody");
        rows.ShouldAllBe(row => row.AccountName == $"{name} Nominees");
        rows.ShouldAllBe(row => row.CustodianConnection == "simulator");
        rows.Select(row => row.AccountNumber).ShouldBe(
            [$"SIM-{code.ToUpperInvariant()}-CUSTODY-NZD", $"SIM-{code.ToUpperInvariant()}-CUSTODY-USD"]);
    }

    private static async Task<Tenant> EnsureTenantAsync(ApplicationDbContext db, string code, string name)
    {
        var existing = await db.Tenants.SingleOrDefaultAsync(row => row.Code == code);
        if (existing is not null)
        {
            return existing;
        }

        var tenant = Tenant.Create(name, code, Currency.Create("NZD", "New Zealand Dollar", 2));
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
