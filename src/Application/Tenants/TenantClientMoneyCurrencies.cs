using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Tenants;

internal static class TenantClientMoneyCurrencies
{
    /// <summary>
    /// The currencies in which each of <paramref name="tenantIds"/> has an active client money account, sorted A–Z
    /// (client-money-accounts R23). A tenant without one maps to an empty list.
    /// </summary>
    public static async Task<Dictionary<int, IReadOnlyList<string>>> ForTenantsAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive && tenantIds.Contains(row.TenantId))
            .Select(row => new { row.TenantId, row.Currency })
            .Distinct()
            .ToListAsync(cancellationToken);

        return tenantIds.Distinct().ToDictionary(
            id => id,
            id => (IReadOnlyList<string>)rows.Where(row => row.TenantId == id)
                .Select(row => row.Currency)
                .Order(StringComparer.Ordinal)
                .ToList());
    }

}
