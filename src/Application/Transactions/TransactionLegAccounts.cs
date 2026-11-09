using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// The leg accounts of each Transaction: one row per distinct (Transaction, Account) pair
/// that at least one cash or security leg books into.
/// </summary>
internal static class TransactionLegAccounts
{
    public static IQueryable<TransactionLegAccount> Query(IApplicationDbContext db) =>
        db.TransactionCashLegs
            .Select(leg => new TransactionLegAccount { TransactionId = leg.TransactionId, AccountId = leg.AccountId })
            .Union(db.TransactionSecurityLegs
                .Select(leg => new TransactionLegAccount { TransactionId = leg.TransactionId, AccountId = leg.AccountId }));
}

internal sealed class TransactionLegAccount
{
    public int TransactionId { get; init; }

    public int AccountId { get; init; }
}
