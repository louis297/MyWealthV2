using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// fixes/ledger-write-guards.md Decision 4: a caller's cash amount fits the Account currency's decimal places.
/// The comparison is by value, as in FX conversions R11.
/// </summary>
internal static class CashAmountPlaces
{
    public static void EnsureFits(ICurrencyCatalog currencies, string currency, decimal amount)
    {
        var places = currencies.TryGet(currency)?.DecimalPlaces
                     ?? throw new ValidationException([
                         new ValidationFailure(string.Empty, $"Currency {currency} is not in the catalog.")
                     ]);
        if (!FxConversionPricing.FitsDecimalPlaces(amount, places))
        {
            throw new ValidationException([
                new ValidationFailure("Amount", $"Amount can have at most {places} decimal places.")
            ]);
        }
    }
}
