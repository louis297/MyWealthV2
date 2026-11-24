using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Custodian;

/// <summary>In-kind-transfers R46: the safekeeping account an order is placed with.</summary>
internal static class OrderSafekeeping
{
    /// <summary>The Tenant's active safekeeping account in <paramref name="quoteCurrency"/>, or none; having none never
    /// refuses an order.</summary>
    public static Task<SafekeepingAccount?> ActiveAsync(
        IApplicationDbContext db, int tenantId, string quoteCurrency, CancellationToken cancellationToken)
    {
        var currency = quoteCurrency.Trim().ToUpperInvariant();
        return db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.IsActive && row.Currency == currency, cancellationToken);
    }
}
