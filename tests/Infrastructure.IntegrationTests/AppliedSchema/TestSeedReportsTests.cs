using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class TestSeedReportsTests
{
    [Test]
    public async Task Seed_GrantsBothReportsToDemoTenantAndIsIdempotent()
    {
        await using var db = OpenDb();
        await EnsureDemoTenantAsync(db);
        await EnsureTenantAsync(db, "Reports Other Advisory", "reports-other");
        var prices = new MarketDataMock();

        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var first = await LoadGrantsAsync(db, DevelopmentIdentitySeeder.DemoTenantCode);
        await TestSeed.SeedAsync(db, prices, CancellationToken.None);
        var second = await LoadGrantsAsync(db, DevelopmentIdentitySeeder.DemoTenantCode);

        first.Select(row => row.Code).ShouldBe(["adviser-balances", "customer-transactions"]);
        second.Select(row => row.Id).ShouldBe(first.Select(row => row.Id));
        (await LoadGrantsAsync(db, "reports-other")).ShouldBeEmpty();
    }

    private static async Task EnsureDemoTenantAsync(ApplicationDbContext db)
    {
        await EnsureTenantAsync(
            db,
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode);
    }

    private static async Task EnsureTenantAsync(ApplicationDbContext db, string name, string code)
    {
        if (await db.Tenants.AnyAsync(row => row.Code == code))
        {
            return;
        }

        db.Tenants.Add(Tenant.Create(name, code, Currency.Create("NZD", "New Zealand Dollar", 2)));
        await db.SaveChangesAsync();
    }

    private static async Task<List<GrantRow>> LoadGrantsAsync(ApplicationDbContext db, string tenantCode)
    {
        var tenant = await db.Tenants.SingleAsync(row => row.Code == tenantCode);
        return await (
            from grant in db.TenantReports.AsNoTracking()
            join report in db.Reports.AsNoTracking() on grant.ReportId equals report.Id
            where grant.TenantId == tenant.Id
            orderby report.Code
            select new GrantRow(grant.Id, report.Code)
        ).ToListAsync();
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed record GrantRow(int Id, string Code);
}
