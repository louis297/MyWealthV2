using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.AccountClosures;

/// <summary>
/// Account closure R11, R12: an Account is <b>closing</b> while it has an account closure in progress, and then
/// accepts no hand write. Each hand write calls the check under the Account lock, directly after the Closed-account
/// check. It reads the closure rows, never an Account entity loaded before the lock.
/// </summary>
public static class AccountClosing
{
    public const string Message = "This account is being closed and accepts no new activity.";

    public static Task<bool> IsClosingAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        db.AccountClosures.IgnoreQueryFilters()
            .AnyAsync(row => row.AccountId == accountId && row.Status == AccountClosureStatus.InProgress, cancellationToken);

    /// <summary>R12: 400 under the empty <c>errors</c> key when any of <paramref name="accountIds"/> is closing.</summary>
    public static async Task EnsureNotClosingAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> accountIds,
        CancellationToken cancellationToken)
    {
        if (await db.AccountClosures.IgnoreQueryFilters()
                .AnyAsync(row => accountIds.Contains(row.AccountId) && row.Status == AccountClosureStatus.InProgress,
                    cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(string.Empty, Message)]);
        }
    }

    public static Task EnsureNotClosingAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        EnsureNotClosingAsync(db, [accountId], cancellationToken);
}
