using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts;

internal static class AccountScope
{
    public static int? AssignedAdviserInternalId(ICurrentUser currentUser) =>
        currentUser.Role == UserRole.Adviser ? currentUser.Person?.Id : null;

    /// <summary>
    /// Null when the caller is not a Customer. A Customer is limited to this person id
    /// (0 when the person is missing, which matches no account).
    /// </summary>
    public static int? SelfCustomerId(ICurrentUser currentUser) =>
        currentUser.Role == UserRole.Customer ? currentUser.Person?.Id ?? 0 : null;

    /// <summary>
    /// The Customer <paramref name="customerId"/> as the caller may read their accounts (net-worth R1, R9;
    /// tax-year-events R1): a TenantAdmin any Customer of the Tenant, an Adviser an assigned one, a Customer themselves,
    /// a SystemAdmin a Customer of any Tenant. Any other id is not found.
    /// </summary>
    public static async Task<User> ReadableCustomerAsync(
        IApplicationDbContext db, ICurrentUser currentUser, Guid customerId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.Customer && customerId != currentUser.PublicId)
        {
            throw new NotFoundException("Customer", customerId);
        }

        var assignedAdviserId = AssignedAdviserInternalId(currentUser);
        var customers = db.Users.AsNoTracking()
            .Where(user => user.PublicId == customerId && user.Role == UserRole.Customer);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
            customers = customers.Where(user => user.TenantId == tenantId);
        }

        if (assignedAdviserId is not null)
        {
            customers = customers.Where(user => user.AdviserId == assignedAdviserId);
        }

        return await customers.SingleOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Customer", customerId);
    }

    public static IQueryable<Account> ManagedAccounts(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var assignedAdviserId = AssignedAdviserInternalId(currentUser);
        var selfCustomerId = SelfCustomerId(currentUser);
        var query =
            from account in db.Accounts
            join customer in db.Users on account.CustomerId equals customer.Id
            where (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(account => account.TenantId == currentUser.TenantId);
        }

        return query;
    }
}
