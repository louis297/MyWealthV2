using System.Globalization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Orders R45: cash leaves a Custodial account for somewhere else only from its withdrawable cash. After the from side
/// of an <c>InternalTransfer</c> or an <c>FxConversion</c>, still under the Account lock, the cash <c>SUM</c> must
/// cover the reserved cash and the proceeds of sales that have not settled. Without it, unsettled cash could move to
/// another account of the Customer and be withdrawn there.
/// </summary>
internal static class WithdrawableCashGuard
{
    public static string Message(decimal unsettled, string currency, int decimalPlaces) =>
        $"Cash from sales that have not settled cannot leave the account: {unsettled.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)} {currency} settles later.";

    /// <summary>Throws 400 under <paramref name="errorKey"/> when <paramref name="cashSumAfter"/> covers the reserved
    /// cash but not the unsettled cash as well. A <c>SUM</c> below the reserved cash is left to
    /// <see cref="AvailableCashGuard"/> and to the caller's own refusal.</summary>
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        ICurrencyCatalog currencies,
        Account account,
        decimal cashSumAfter,
        CancellationToken cancellationToken,
        string errorKey = "")
    {
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            return;
        }

        var unsettled = await AccountCash.UnsettledAsync(db, account.Id, cancellationToken);
        if (unsettled <= 0m)
        {
            return;
        }

        var reserved = await AccountCash.ReservedAsync(db, account.Id, cancellationToken);
        if (!AvailableCash.Allows(cashSumAfter, reserved) || AvailableCash.AllowsLeaving(cashSumAfter, reserved, unsettled))
        {
            return;
        }

        var currency = account.Currency.Trim();
        var places = currencies.TryGet(currency)?.DecimalPlaces ?? 2;
        throw new ValidationException([new ValidationFailure(errorKey, Message(unsettled, currency, places))]);
    }
}
