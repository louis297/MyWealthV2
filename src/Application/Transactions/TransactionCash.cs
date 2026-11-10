using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Transactions;

internal static class TransactionCash
{
    public static async Task<decimal> SumAsync(
        IApplicationDbContext db,
        int accountId,
        CancellationToken cancellationToken)
    {
        var sum = await (
            from leg in db.TransactionCashLegs
            where leg.AccountId == accountId
            select (decimal?)leg.Amount).SumAsync(cancellationToken);
        return sum ?? 0m;
    }

    public static Task<int> CountAsync(
        IApplicationDbContext db,
        int accountId,
        CancellationToken cancellationToken) =>
        TransactionLegAccounts.Query(db).CountAsync(row => row.AccountId == accountId, cancellationToken);
}
