using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// Reusable two-tenant insert for later tenant-scoped slices (cross-tenant 404).
/// This slice's own isolation gate is policy 403, not 404-for-other-tenant.
/// </summary>
public static class TwoTenantFixture
{
    public static async Task<(Tenant A, Tenant B)> InsertAsync(ApplicationDbContext db)
    {
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = Tenant.Create($"Firm A {suffix}", $"firma-{suffix}", nzd);
        var tenantB = Tenant.Create($"Firm B {suffix}", $"firmb-{suffix}", nzd);
        db.Tenants.AddRange(tenantA, tenantB);
        await db.SaveChangesAsync();
        return (tenantA, tenantB);
    }

    public static async Task<(User AdminA, User AdminB)> InsertTenantAdminsAsync(
        ApplicationDbContext db,
        Tenant tenantA,
        Tenant tenantB)
    {
        var adminA = await InsertTenantAdminAsync(db, tenantA, "Alex A", "alex@a.example");
        var adminB = await InsertTenantAdminAsync(db, tenantB, "Blair B", "blair@b.example");
        return (adminA, adminB);
    }

    public static async Task<(User AdviserA, User AdviserB)> InsertAdvisersAsync(
        ApplicationDbContext db,
        Tenant tenantA,
        Tenant tenantB)
    {
        var adviserA = await InsertPersonAsync(
            db, tenantA, UserRole.Adviser, "Sam A", "sam@a.example");
        var adviserB = await InsertPersonAsync(
            db, tenantB, UserRole.Adviser, "Sam B", "sam@b.example");
        return (adviserA, adviserB);
    }

    public static async Task<(User CustomerA, User CustomerB)> InsertCustomersAsync(
        ApplicationDbContext db,
        Tenant tenantA,
        Tenant tenantB,
        User adviserA,
        User adviserB)
    {
        var customerA = await InsertPersonAsync(
            db, tenantA, UserRole.Customer, "Jordan A", "jordan@a.example", adviserA.Id);
        var customerB = await InsertPersonAsync(
            db, tenantB, UserRole.Customer, "Jordan B", "jordan@b.example", adviserB.Id);
        return (customerA, customerB);
    }

    public static async Task<(User AdviserA, User AdviserB, User CustomerA, User CustomerB)>
        InsertTwoAdvisersWithCustomersAsync(ApplicationDbContext db, Tenant tenant)
    {
        var adviserA = await InsertPersonAsync(
            db, tenant, UserRole.Adviser, "Sam A", "sama@example");
        var adviserB = await InsertPersonAsync(
            db, tenant, UserRole.Adviser, "Sam B", "samb@example");
        var customerA = await InsertPersonAsync(
            db, tenant, UserRole.Customer, "Jordan A", "jordana@example", adviserA.Id);
        var customerB = await InsertPersonAsync(
            db, tenant, UserRole.Customer, "Jordan B", "jordanb@example", adviserB.Id);
        return (adviserA, adviserB, customerA, customerB);
    }

    private static async Task<User> InsertTenantAdminAsync(
        ApplicationDbContext db,
        Tenant tenant,
        string name,
        string email) =>
        await InsertPersonAsync(db, tenant, UserRole.TenantAdmin, name, email);

    private static async Task<User> InsertPersonAsync(
        ApplicationDbContext db,
        Tenant tenant,
        UserRole role,
        string name,
        string email,
        int? adviserId = null)
    {
        var publicId = Guid.NewGuid();
        var identity = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = email,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };
        db.Users.Add(identity);
        await db.SaveChangesAsync();

        var person = User.Create(
            role,
            name,
            email,
            identity.Id,
            tenant.Id,
            adviserId,
            publicId: publicId);
        db.DomainUsers.Add(person);
        await db.SaveChangesAsync();
        return person;
    }
}
