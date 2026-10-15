using System.Globalization;

namespace MyWealthV2.Application.Reports;

public static class CashAmountText
{
    public static string Format(decimal amount, string currency, int decimalPlaces) =>
        string.Concat(
            amount.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture),
            " ",
            currency);
}
