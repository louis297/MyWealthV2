using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.ClientMoneyAccounts;

/// <summary>The checks a client money account must pass before it becomes active (R4, R5).</summary>
internal static class ClientMoneyAccountRules
{
    public const string AccountNumberInUseMessage =
        "This account number is already in use by an active client money account.";

    public static string CurrencyTakenMessage(string currency) =>
        $"The tenant already has an active client money account in {currency}.";

    /// <summary>
    /// Throws a validation error when the tenant already has another active row in <paramref name="currency"/> (R4),
    /// or <paramref name="accountNumber"/> is active on another row of any tenant (R5).
    /// </summary>
    public static async Task EnsureCanBeActiveAsync(
        IApplicationDbContext db,
        int tenantId,
        string currency,
        string accountNumber,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var active = db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.IsActive && row.Id != (exceptId ?? 0));

        if (await active.AnyAsync(row => row.TenantId == tenantId && row.Currency == currency, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(ClientMoneyAccount.Currency), CurrencyTakenMessage(currency))
            ]);
        }

        // The database collation compares the account number without regard to case.
        if (await active.AnyAsync(row => row.AccountNumber == accountNumber, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(ClientMoneyAccount.AccountNumber), AccountNumberInUseMessage)
            ]);
        }
    }
}
