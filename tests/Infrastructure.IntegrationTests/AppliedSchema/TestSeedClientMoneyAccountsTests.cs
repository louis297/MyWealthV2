using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Client money accounts §9.1: TestSeed gives each seeded firm an active USD and NZD client money account.</summary>
public class TestSeedClientMoneyAccountsTests
{
    [TestCase(DevelopmentIdentitySeeder.DemoTenantCode, DevelopmentIdentitySeeder.DemoTenantName)]
    [TestCase(DevelopmentIdentitySeeder.SecondTenantCode, DevelopmentIdentitySeeder.SecondTenantName)]
    public async Task SeedTwice_GivesTheFirmOneActiveUsdAndOneActiveNzdRow(string code, string name)
    {
        await using var db = OpenDb();
        var tenant = await EnsureTenantAsync(db, code, name);

        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var rows = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => row.TenantId == tenant.Id)
            .OrderBy(row => row.Currency)
            .ToListAsync();
        rows.Select(row => row.Currency).ShouldBe(["NZD", "USD"]);
        rows.ShouldAllBe(row => row.IsActive);
        rows.ShouldAllBe(row => row.BankName == "Simulator Bank");
        rows.ShouldAllBe(row => row.AccountName == $"{name} Client Funds Trust Account");
        rows.Select(row => row.AccountNumber).ShouldBe(
            [$"SIM-{code.ToUpperInvariant()}-NZD", $"SIM-{code.ToUpperInvariant()}-USD"]);
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
