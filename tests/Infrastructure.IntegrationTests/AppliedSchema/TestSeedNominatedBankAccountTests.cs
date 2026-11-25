using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Withdrawals §9.1: TestSeed gives the demo Customer one Verified USD nominated bank account, added by the
/// demo Adviser and verified by the demo TenantAdmin, so a withdrawal can be requested without a check.</summary>
[Parallelizable]
public class TestSeedNominatedBankAccountTests
{
    [Test]
    public async Task SeedTwice_GivesTheDemoCustomerOneVerifiedUsdAccount()
    {
        await using var db = OpenDb();
        var tenant = await EnsureTenantAsync(db, DevelopmentIdentitySeeder.DemoTenantCode, DevelopmentIdentitySeeder.DemoTenantName);
        var admin = await EnsurePersonAsync(db, tenant, UserRole.TenantAdmin, "Demo Admin", DevelopmentIdentitySeeder.TenantAdminEmail);
        var adviser = await EnsurePersonAsync(db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        var customer = await EnsurePersonAsync(db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);

        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var row = (await db.NominatedBankAccounts.AsNoTracking()
            .Where(item => item.CustomerId == customer.Id && item.AccountNumber == "SIM-PAYEE-0001")
            .ToListAsync()).ShouldHaveSingleItem();
        row.TenantId.ShouldBe(tenant.Id);
        row.Currency.ShouldBe("USD");
        row.Status.ShouldBe(NominatedBankAccountStatus.Verified);
        row.BankName.ShouldBe("Simulator Bank");
        row.AccountName.ShouldBe(customer.Name);
        row.AddedByUserId.ShouldBe(adviser.Id);
        row.ReviewedByUserId.ShouldBe(admin.Id);
        row.ReviewNote.ShouldNotBeNullOrWhiteSpace();
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
