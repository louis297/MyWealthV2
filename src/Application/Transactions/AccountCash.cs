using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Withdrawals R35: an Account's cash balance, the cash reserved for its open withdrawals, and what is available.
/// Writes read it under the Account lock (R22, R36).
/// </summary>
internal static class AccountCash
{
    private static readonly WithdrawalStatus[] Open =
        Enum.GetValues<WithdrawalStatus>().Where(Withdrawal.IsOpenStatus).ToArray();

    public static Task<decimal> SumAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        TransactionCash.SumAsync(db, accountId, cancellationToken);

    public static async Task<decimal> ReservedAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        await db.Withdrawals
            .Where(row => row.AccountId == accountId && Open.Contains(row.Status))
            .SumAsync(row => (decimal?)row.Amount, cancellationToken) ?? 0m;

    public static async Task<decimal> AvailableAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        AvailableCash.Of(await SumAsync(db, accountId, cancellationToken), await ReservedAsync(db, accountId, cancellationToken));

    /// <summary>Reserved cash of each Account in <paramref name="accountIds"/>; an Account without one is missing.</summary>
    public static async Task<Dictionary<int, decimal>> ReservedAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken) =>
        accountIds.Count == 0
            ? []
            : await db.Withdrawals.AsNoTracking()
                .Where(row => accountIds.Contains(row.AccountId) && Open.Contains(row.Status))
                .GroupBy(row => row.AccountId)
                .Select(group => new { AccountId = group.Key, Sum = group.Sum(row => row.Amount) })
                .ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);
}
