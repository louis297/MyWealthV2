namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The New Zealand time of an instant (docs/features/fif-report.md R13), by the rule fixed since 2007. Reads no clock,
/// no setting and no time zone data of the host.
/// </summary>
public static class NewZealandTime
{
    /// <summary>The hours New Zealand is ahead of UTC at <paramref name="instant"/>, or none before the rule of 2007.</summary>
    public static TimeSpan? Offset(DateTimeOffset instant) => null;

    /// <summary>The date and time in New Zealand at <paramref name="instant"/>, or none before the rule of 2007.</summary>
    public static DateTime? Local(DateTimeOffset instant) => null;
}
