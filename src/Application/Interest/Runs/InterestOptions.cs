namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R32: configuration section <c>Interest</c>.</summary>
public sealed class InterestOptions
{
    public const string SectionName = "Interest";

    public const string AllowFutureAsOfDatesKey = SectionName + ":" + nameof(AllowFutureAsOfDates);

    /// <summary>When true, a manual run may have an as-of date after yesterday (UTC). Off by default; on in Development.</summary>
    public bool AllowFutureAsOfDates { get; set; }

    /// <summary>R32: a host outside Development with the option on fails at start-up, before it listens.</summary>
    public static void EnsureAllowedOnHost(bool isDevelopment, bool allowFutureAsOfDates)
    {
        if (allowFutureAsOfDates && !isDevelopment)
        {
            throw new InvalidOperationException(
                $"{AllowFutureAsOfDatesKey} is on outside Development. Future as-of dates post interest for days that have not happened; turn the option off.");
        }
    }
}
