namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The New Zealand time of an instant (docs/features/fif-report.md R13), by the rule fixed since 2007: 13 hours ahead
/// of UTC from 14:00 UTC on the Saturday before the last Sunday of September (02:00 standard time becomes 03:00) until
/// 14:00 UTC on the Saturday before the first Sunday of April (03:00 daylight time becomes 02:00), and 12 hours ahead
/// otherwise. It is the rule behind the fixed instants of <see cref="TaxYear"/>. Reads no clock, no setting and no time
/// zone data of the host.
/// </summary>
public static class NewZealandTime
{
    private static readonly TimeSpan Standard = TimeSpan.FromHours(12);
    private static readonly TimeSpan Daylight = TimeSpan.FromHours(13);

    /// <summary>The first instant the rule holds for: daylight time started at 14:00 UTC on 29 September 2007.</summary>
    public static readonly DateTimeOffset FirstInstant = new(2007, 9, 29, 14, 0, 0, TimeSpan.Zero);

    /// <summary>The hours New Zealand is ahead of UTC at <paramref name="instant"/>, or none before the rule of 2007.</summary>
    public static TimeSpan? Offset(DateTimeOffset instant)
    {
        if (instant < FirstInstant)
            return null;

        var year = instant.UtcDateTime.Year;
        return instant < DaylightEnds(year) || instant >= DaylightStarts(year) ? Daylight : Standard;
    }

    /// <summary>The date and time in New Zealand at <paramref name="instant"/>, or none before the rule of 2007.</summary>
    public static DateTime? Local(DateTimeOffset instant) =>
        Offset(instant) is { } offset
            ? DateTime.SpecifyKind(instant.UtcDateTime + offset, DateTimeKind.Unspecified)
            : null;

    private static DateTimeOffset DaylightEnds(int year)
    {
        var firstSunday = new DateTime(year, 4, 1);
        while (firstSunday.DayOfWeek != DayOfWeek.Sunday)
            firstSunday = firstSunday.AddDays(1);
        return At1400UtcOn(firstSunday.AddDays(-1));
    }

    private static DateTimeOffset DaylightStarts(int year)
    {
        var lastSunday = new DateTime(year, 9, 30);
        while (lastSunday.DayOfWeek != DayOfWeek.Sunday)
            lastSunday = lastSunday.AddDays(-1);
        return At1400UtcOn(lastSunday.AddDays(-1));
    }

    private static DateTimeOffset At1400UtcOn(DateTime day) => new(day.Year, day.Month, day.Day, 14, 0, 0, TimeSpan.Zero);
}
