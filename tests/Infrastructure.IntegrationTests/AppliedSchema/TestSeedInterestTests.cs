using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Interest run §9.1: TestSeed gives the demo tenant a USD and an NZD interest rate schedule after the cut-off
/// date, and the demo Customer a tax profile (IRD number held, 17.5 %).</summary>
[Parallelizable]
public class TestSeedInterestTests
{
    [Test]
    public async Task SeedTwice_GivesTheDemoTenantOneUsdAndOneNzdSchedule_AfterTheCutOff()
    {
        await using var db = OpenDb();
        var (tenant, _, _) = await EnsureDemoAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await TestSeed.SeedAsync(db, CancellationToken.None);
        var first = await SchedulesAsync(db, tenant.Id);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        var second = await SchedulesAsync(db, tenant.Id);

        first.Select(row => row.Currency).ShouldBe(["NZD", "USD"]);
        second.Select(row => row.PublicId).ShouldBe(first.Select(row => row.PublicId));
        first.ShouldAllBe(row => row.EffectiveFrom > today && row.CreatedBy == TestSeed.CreatedBy && row.BandRows.Count >= 1);
    }

    [Test]
    public async Task SeedTwice_GivesTheDemoCustomerOneTaxProfile_IrdHeld_At17Point5()
    {
        await using var db = OpenDb();
        var (_, admin, customer) = await EnsureDemoAsync(db);

        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var profile = (await db.CustomerTaxProfiles.AsNoTracking().Where(row => row.UserId == customer.Id).ToListAsync())
            .ShouldHaveSingleItem();
        (profile.TaxResidency, profile.IrdNumberHeld, profile.RwtRatePercent, profile.UpdatedByUserId)
            .ShouldBe((TaxResidency.NewZealand, true, (decimal?)17.5m, admin.Id));
        profile.CreatedBy.ShouldBe(TestSeed.CreatedBy);
    }

    private static Task<List<InterestRateSchedule>> SchedulesAsync(ApplicationDbContext db, int tenantId) =>
        db.InterestRateSchedules.AsNoTracking().Include(row => row.BandRows)
            .Where(row => row.TenantId == tenantId).OrderBy(row => row.Currency).ToListAsync();

    private static async Task<(Tenant Tenant, User Admin, User Customer)> EnsureDemoAsync(ApplicationDbContext db)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        if (tenant is null)
        {
            tenant = Tenant.Create(DevelopmentIdentitySeeder.DemoTenantName, DevelopmentIdentitySeeder.DemoTenantCode,
                Currency.Create("NZD", "New Zealand Dollar", 2));
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        var admin = await EnsurePersonAsync(db, tenant, UserRole.TenantAdmin, "Demo Admin", DevelopmentIdentitySeeder.TenantAdminEmail);
        var adviser = await EnsurePersonAsync(db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        var customer = await EnsurePersonAsync(db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        return (tenant, admin, customer);
    }

    private static async Task<User> EnsurePersonAsync(
        ApplicationDbContext db,
        Tenant tenant,
        UserRole role,
        string name,
        string email,
        int? adviserId = null)
    {
        var existing = await db.DomainUsers.SingleOrDefaultAsync(row => row.TenantId == tenant.Id && row.Email == email);
        if (existing is not null)
        {
            return existing;
        }

        var publicId = Guid.NewGuid();
        var identity = new ApplicationUser { UserName = publicId.ToString(), Email = email, EmailConfirmed = true, TenantId = tenant.Id };
        db.Users.Add(identity);
        await db.SaveChangesAsync();
        var person = User.Create(role, name, email, identity.Id, tenant.Id, adviserId, publicId: publicId);
        db.DomainUsers.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);
}
