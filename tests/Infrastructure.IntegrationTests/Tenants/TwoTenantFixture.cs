using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;

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
}
