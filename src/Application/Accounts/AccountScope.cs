using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Accounts;

internal static class AccountScope
{
    public static int? AssignedAdviserInternalId(ICurrentUser currentUser) =>
        currentUser.Role == UserRole.Adviser ? currentUser.Person?.Id : null;

    public static IQueryable<Account> ManagedAccounts(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var assignedAdviserId = AssignedAdviserInternalId(currentUser);
        var query =
            from account in db.Accounts
            join customer in db.Users on account.CustomerId equals customer.Id
            where assignedAdviserId == null || customer.AdviserId == assignedAdviserId
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(account => account.TenantId == currentUser.TenantId);
        }

        return query;
    }
}
