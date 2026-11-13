using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Portfolios;

/// <summary>
/// Portfolios amendment 2026-11-13 part 2. The caller's Book is <see cref="AccountScope.ManagedAccounts"/>:
/// for an Adviser, the accounts of their assigned Customers; for TenantAdmin and SystemAdmin, every account.
/// </summary>
internal static class PortfolioBook
{
    /// <summary>PublicIds of the portfolio's Active assignments whose Account is in the caller's Book (PB2, PB8).</summary>
    public static Task<List<Guid>> ActiveAccountIdsAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        int portfolioId,
        CancellationToken cancellationToken) =>
        (from assignment in db.AccountPortfolioAssignments
         join account in AccountScope.ManagedAccounts(db, currentUser) on assignment.AccountId equals account.Id
         where assignment.PortfolioId == portfolioId && assignment.Status == AssignmentStatus.Active
         select account.PublicId).ToListAsync(cancellationToken);

    /// <summary>
    /// PB3 / PB4: an Adviser may not change or disable a Shared portfolio, one with an Active assignment on an
    /// Account outside the Adviser's Book. TenantAdmin and SystemAdmin are not limited (PB5).
    /// </summary>
    public static async Task EnsureNotSharedAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        int portfolioId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.Adviser)
        {
            return;
        }

        var book = AccountScope.ManagedAccounts(db, currentUser).Select(account => account.Id);
        var shared = await db.AccountPortfolioAssignments.AnyAsync(
            assignment => assignment.PortfolioId == portfolioId
                          && assignment.Status == AssignmentStatus.Active
                          && !book.Contains(assignment.AccountId),
            cancellationToken);
        if (shared)
        {
            throw new PortfolioSharedException();
        }
    }
}
