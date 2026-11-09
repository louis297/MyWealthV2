using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Read visibility through leg accounts (leg-accounts R7): a caller sees a Transaction when it can see
/// at least one of its leg accounts, and sees only the legs on those accounts.
/// </summary>
internal static class TransactionVisibility
{
    /// <summary>Internal ids of the Accounts in <paramref name="tenantId"/> the caller can read.</summary>
    public static IQueryable<int> VisibleAccountIds(IApplicationDbContext db, ICurrentUser currentUser, int tenantId)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        return
            from account in db.Accounts
            join customer in db.Users on account.CustomerId equals customer.Id
            where account.TenantId == tenantId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select account.Id;
    }

    /// <summary>Transactions with at least one leg on an Account in <paramref name="accountIds"/>.</summary>
    public static IQueryable<Domain.Entities.Transaction> WithLegOn(
        this IQueryable<Domain.Entities.Transaction> transactions,
        IQueryable<int> accountIds) =>
        transactions.Where(transaction =>
            transaction.CashLegs.Any(leg => accountIds.Contains(leg.AccountId))
            || transaction.SecurityLegs.Any(leg => accountIds.Contains(leg.AccountId)));
}
