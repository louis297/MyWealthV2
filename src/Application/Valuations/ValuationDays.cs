using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Valuations;

/// <summary>
/// The days a Custodial account has a stored valuation for (valuation-run R3; valuation-history R6): from its first
/// day, the UTC date it was opened, and for a Closed account through the UTC day its latest completed closure
/// completed. An Open account, or a Closed one with no completed closure, has no last day.
/// </summary>
public static class ValuationDays
{
    public static DateOnly FirstDay(DateTimeOffset created) => LedgerDay.Of(created);

    public static DateOnly? LastDay(AccountStatus status, DateTimeOffset? lastClosureCompletedAt) =>
        status == AccountStatus.Closed && lastClosureCompletedAt is { } at ? LedgerDay.Of(at) : null;

    public static bool IsExpected(DateOnly day, DateOnly firstDay, DateOnly? lastDay) =>
        day >= firstDay && (lastDay is not { } last || day <= last);

    /// <summary>The instant the latest completed closure of each of <paramref name="accountIds"/> completed.</summary>
    public static async Task<Dictionary<int, DateTimeOffset?>> LastClosuresAsync(
        IApplicationDbContext db, int tenantId, IReadOnlyCollection<int> accountIds, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        return await db.AccountClosures.IgnoreQueryFilters()
            .Where(closure => closure.TenantId == tenantId
                              && accountIds.Contains(closure.AccountId)
                              && closure.Status == AccountClosureStatus.Completed)
            .GroupBy(closure => closure.AccountId)
            .Select(group => new { AccountId = group.Key, CompletedAt = group.Max(closure => closure.CompletedAt) })
            .ToDictionaryAsync(row => row.AccountId, row => row.CompletedAt, cancellationToken);
    }
}
