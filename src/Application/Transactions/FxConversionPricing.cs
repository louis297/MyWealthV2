namespace MyWealthV2.Application.Transactions;

/// <summary>FX conversions R10 and R11 checks that need no database.</summary>
public static class FxConversionPricing
{
    /// <summary>R10: the most calendar days allowed between the rate's as-of date and the date of bookedAt.</summary>
    public const int MaxRateAgeDays = 4;

    /// <summary>The calendar date of <paramref name="bookedAt"/> in its own offset (same rule as the rate read).</summary>
    public static DateOnly DateOf(DateTimeOffset bookedAt) => throw new NotImplementedException();

    public static bool IsStale(DateOnly rateAsOfDate, DateTimeOffset bookedAt) => throw new NotImplementedException();

    public static bool FitsDecimalPlaces(decimal amount, int decimalPlaces) => throw new NotImplementedException();
}
