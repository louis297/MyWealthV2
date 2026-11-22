using System.Globalization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Withdrawals R36, R37, orders R39: after a write that lowers cash on a Custodial account, still under the Account
/// lock, the cash <c>SUM</c> must cover the cash reserved for open withdrawals and live buy orders. External accounts
/// keep posting R9 only.
/// </summary>
internal static class AvailableCashGuard
{
    public static string Message(decimal reserved, string currency, int decimalPlaces) =>
        $"Available cash cannot become negative: {reserved.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)} {currency} is reserved for withdrawals and orders.";

    /// <summary>Throws 400 under <paramref name="errorKey"/> when <paramref name="cashSumAfter"/> is at or above zero
    /// but below the Account's reserved cash. A negative <c>SUM</c> is left to the caller's own refusal.</summary>
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        ICurrencyCatalog currencies,
        Account account,
        decimal cashSumAfter,
        CancellationToken cancellationToken,
        string errorKey = "")
    {
        if (account.CustodyMode != CustodyMode.Custodial || cashSumAfter < 0)
        {
            return;
        }

        var reserved = await AccountCash.ReservedAsync(db, account.Id, cancellationToken);
        if (AvailableCash.Allows(cashSumAfter, reserved))
        {
            return;
        }

        var currency = account.Currency.Trim();
        var places = currencies.TryGet(currency)?.DecimalPlaces ?? 2;
        throw new ValidationException([new ValidationFailure(errorKey, Message(reserved, currency, places))]);
    }
}
