using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Valuations;

/// <summary>The Account a valuation read is for (account-valuation R1, valuation-run R1).</summary>
internal static class ValuationAccounts
{
    /// <summary>The Account found as <c>GET /accounts/{id}/holdings</c> finds it: a TenantAdmin's Tenant, an Adviser's
    /// assigned Customers, a Customer's own, any for a SystemAdmin. Any other Account is 404.</summary>
    public static async Task<Account> FindAsync(
        IApplicationDbContext db, ICurrentUser currentUser, Guid accountId, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var accounts =
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == accountId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            var tenantId = currentUser.TenantId ?? throw new NotFoundException("Account", accountId);
            accounts = accounts.Where(account => account.TenantId == tenantId);
        }

        return await accounts.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Account", accountId);
    }
}
