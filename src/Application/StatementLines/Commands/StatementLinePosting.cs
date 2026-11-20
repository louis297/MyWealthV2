using FluentValidation.Results;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>The posting checks a TenantAdmin's match and undo reuse (deposit-matching R26, R30; posting R6).</summary>
internal static class StatementLinePosting
{
    public static async Task EnsureCanPostAsync(IApplicationDbContext db, Account account, CancellationToken cancellationToken)
    {
        if (account.Status == AccountStatus.Closed)
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "Cannot post a transaction while the account is closed. Reopen the account first.")
            ]);
        }

        var customer = await BankDeposits.CustomerAsync(db, account, cancellationToken);
        if (BankDeposits.IsDisabled(customer))
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot create a transaction while the customer is disabled. Enable the customer first.");
        }
    }
}
