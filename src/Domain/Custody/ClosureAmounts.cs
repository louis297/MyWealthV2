namespace MyWealthV2.Domain.Custody;

/// <summary>
/// Account closure R7: the <b>payout</b> of a cash balance is the balance rounded down to a multiple of the
/// currency's minor unit <c>u = 10^−DecimalPlaces</c>; the <b>remainder</b> is the rest, so <c>0 ≤ remainder &lt; u</c>.
/// </summary>
public static class ClosureAmounts
{
    /// <summary>R7: the payout and the remainder of <paramref name="cashSum"/> (≥ 0 on a Custodial account).</summary>
    public static (decimal Payout, decimal Remainder) Split(decimal cashSum, int decimalPlaces)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cashSum);
        var payout = decimal.Round(cashSum, decimalPlaces, MidpointRounding.ToZero);
        return (payout, cashSum - payout);
    }

    /// <summary>True for a cash <c>SUM</c> that is a remainder: <c>0 ≤ cashSum &lt; u</c> (R17).</summary>
    public static bool IsRemainder(decimal cashSum, int decimalPlaces) =>
        cashSum >= 0m && cashSum < MinorUnit(decimalPlaces);

    /// <summary>One minor unit of a currency with <paramref name="decimalPlaces"/>: 0.01 for two, 1 for none.</summary>
    public static decimal MinorUnit(int decimalPlaces)
    {
        var unit = 1m;
        for (var place = 0; place < decimalPlaces; place++)
        {
            unit /= 10m;
        }

        return unit;
    }
}
