using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Dividends R46, R52: the <c>Pending</c> dividend entitlements of accounts.</summary>
internal static class PendingDividends
{
    public static Task<bool> AnyAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        Of(db, [accountId]).AnyAsync(cancellationToken);

    /// <summary>The number of <c>Pending</c> dividend entitlements per account, for the accounts that have one.</summary>
    public static async Task<Dictionary<int, int>> CountAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken) =>
        accountIds.Count == 0
            ? []
            : await Of(db, accountIds)
                .GroupBy(row => row.AccountId)
                .Select(group => new { AccountId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.AccountId, row => row.Count, cancellationToken);

    private static IQueryable<Domain.Entities.CorporateActionEntitlement> Of(IApplicationDbContext db, IReadOnlyCollection<int> accountIds) =>
        db.CorporateActionEntitlements.IgnoreQueryFilters()
            .Where(row => accountIds.Contains(row.AccountId)
                          && row.Status == EntitlementStatus.Pending
                          && row.EntitledQuantity != null);
}
