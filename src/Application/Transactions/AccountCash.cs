using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Withdrawals R35, orders R39: an Account's cash balance, the cash reserved for its open withdrawals and its live
/// buy orders, and what is available. Writes read it under the Account lock (R22, R36). An order stores what it
/// reserves now, 0 when it is not live, so the orders' part is a plain sum.
/// </summary>
internal static class AccountCash
{
    private static readonly WithdrawalStatus[] Open =
        Enum.GetValues<WithdrawalStatus>().Where(Withdrawal.IsOpenStatus).ToArray();

    public static Task<decimal> SumAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        TransactionCash.SumAsync(db, accountId, cancellationToken);

    public static async Task<decimal> ReservedAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken)
    {
        var withdrawals = await db.Withdrawals
            .Where(row => row.AccountId == accountId && Open.Contains(row.Status))
            .SumAsync(row => (decimal?)row.Amount, cancellationToken) ?? 0m;
        var orders = await db.Orders
            .Where(row => row.AccountId == accountId)
            .SumAsync(row => (decimal?)row.ReservedCash, cancellationToken) ?? 0m;
        return withdrawals + orders;
    }

    /// <summary>Orders R36: the reserved cash of the Account without what <paramref name="orderId"/> reserves, for a
    /// fill of that order, whose own reservation is worked out again.</summary>
    public static async Task<decimal> ReservedExceptOrderAsync(
        IApplicationDbContext db,
        int accountId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var withdrawals = await db.Withdrawals
            .Where(row => row.AccountId == accountId && Open.Contains(row.Status))
            .SumAsync(row => (decimal?)row.Amount, cancellationToken) ?? 0m;
        var orders = await db.Orders
            .Where(row => row.AccountId == accountId && row.Id != orderId)
            .SumAsync(row => (decimal?)row.ReservedCash, cancellationToken) ?? 0m;
        return withdrawals + orders;
    }

    public static async Task<decimal> AvailableAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        AvailableCash.Of(await SumAsync(db, accountId, cancellationToken), await ReservedAsync(db, accountId, cancellationToken));

    /// <summary>Reserved cash of each Account in <paramref name="accountIds"/>; an Account without one is missing.</summary>
    public static async Task<Dictionary<int, decimal>> ReservedAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        var reserved = await db.Withdrawals.AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId) && Open.Contains(row.Status))
            .GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Sum = group.Sum(row => row.Amount) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);
        var orders = await db.Orders.AsNoTracking()
            .Where(row => accountIds.Contains(row.AccountId) && row.ReservedCash > 0)
            .GroupBy(row => row.AccountId)
            .Select(group => new { AccountId = group.Key, Sum = group.Sum(row => row.ReservedCash) })
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            reserved[order.AccountId] = reserved.GetValueOrDefault(order.AccountId) + order.Sum;
        }

        return reserved;
    }
}
