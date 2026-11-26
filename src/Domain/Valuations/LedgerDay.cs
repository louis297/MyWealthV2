namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// Which UTC day a <c>bookedAt</c> belongs to (docs/features/account-valuation.md R2). Day <c>d</c> ends at 00:00 UTC
/// of <c>d + 1</c>; a Transaction counts in day <c>d</c> when it was booked before that instant.
/// </summary>
public static class LedgerDay
{
    /// <summary>00:00 UTC of the day after <paramref name="day"/>.</summary>
    public static DateTimeOffset End(DateOnly day) => new(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The UTC date of <paramref name="instant"/>.</summary>
    public static DateOnly Of(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);
}
