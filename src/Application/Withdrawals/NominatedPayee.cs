using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Withdrawals;

/// <summary>Withdrawals R20: the nominated bank account a withdrawal pays to. Also checks the payee of an account
/// closure's payout (account-closure R8).</summary>
internal static class NominatedPayee
{
    public const string ErrorKey = "NominatedBankAccountId";

    /// <summary>Another Customer's, another tenant's or a missing nominated bank account is 404 for the whole command.</summary>
    public static async Task<NominatedBankAccount> FindAsync(
        IApplicationDbContext db,
        int tenantId,
        int customerId,
        Guid publicId,
        CancellationToken cancellationToken) =>
        await db.NominatedBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PublicId == publicId
                                         && row.TenantId == tenantId
                                         && row.CustomerId == customerId, cancellationToken)
        ?? throw new NotFoundException("Nominated bank account", publicId);

    /// <summary>A payee is Verified and in the Account's currency; otherwise 400 at <see cref="ErrorKey"/>.</summary>
    public static void EnsurePayable(NominatedBankAccount nominated, string accountCurrency)
    {
        if (nominated.Status != NominatedBankAccountStatus.Verified)
        {
            throw Error("The bank account is not verified.");
        }

        if (!string.Equals(nominated.Currency.Trim(), accountCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Error($"The bank account is in {nominated.Currency.Trim()}; this account is {accountCurrency.Trim()}.");
        }
    }

    private static ValidationException Error(string message) => new([new ValidationFailure(ErrorKey, message)]);
}
