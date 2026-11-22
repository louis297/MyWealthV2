using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Interest.Accruals;

/// <summary>Interest-run R35: the sum of each account's daily accrual rows not yet posted.</summary>
internal static class AccruedInterest
{
    public static async Task<Dictionary<int, decimal>> UnpostedAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        return await db.InterestAccruals.AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId) && row.PostedTransactionId == null)
            .GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Sum = group.Sum(row => row.Amount) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);
    }
}
