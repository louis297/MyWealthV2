namespace MyWealthV2.Domain.Custody;

/// <summary>Account closure R7: the payout and the remainder of a cash balance.</summary>
public static class ClosureAmounts
{
    public static (decimal Payout, decimal Remainder) Split(decimal cashSum, int decimalPlaces) =>
        throw new NotImplementedException();

    public static bool IsRemainder(decimal cashSum, int decimalPlaces) => throw new NotImplementedException();

    public static decimal MinorUnit(int decimalPlaces) => throw new NotImplementedException();
}
