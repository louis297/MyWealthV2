using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.ClientMoneyAccounts;

internal static class ClientMoneyAccountLookup
{
    /// <summary>The tenant by PublicId, else 404 (R1).</summary>
    public static async Task<Tenant> TenantAsync(
        IApplicationDbContext db,
        Guid tenantPublicId,
        CancellationToken cancellationToken) =>
        await db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PublicId == tenantPublicId, cancellationToken)
        ?? throw new NotFoundException("Tenant", tenantPublicId);

    /// <summary>
    /// The tracked row <paramref name="id"/> of the tenant <paramref name="tenantPublicId"/>. Unknown tenant, or a
    /// row that is not the tenant's (another tenant's row included) → 404 (R1).
    /// </summary>
    public static async Task<ClientMoneyAccount> FindAsync(
        IApplicationDbContext db,
        Guid tenantPublicId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var tenant = await TenantAsync(db, tenantPublicId, cancellationToken);
        return await db.ClientMoneyAccounts
                   .SingleOrDefaultAsync(row => row.TenantId == tenant.Id && row.PublicId == id, cancellationToken)
               ?? throw new NotFoundException("Client money account", id);
    }
}
