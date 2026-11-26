using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Account valuation R2: a day is a UTC date and ends at 00:00 UTC of the next date.</summary>
public class LedgerDayTests
{
    private static readonly DateOnly Day = new(2026, 3, 5);

    [Test]
    public void End_IsMidnightUtcOfTheNextDay() =>
        LedgerDay.End(Day).ShouldBe(new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero));

    [Test]
    public void TheLastTickOfTheDay_BelongsToTheDay()
    {
        var instant = new DateTimeOffset(2026, 3, 5, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999);

        LedgerDay.Of(instant).ShouldBe(Day);
        (instant < LedgerDay.End(Day)).ShouldBeTrue();
    }

    [Test]
    public void Midnight_BelongsToTheNextDay()
    {
        var instant = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);

        LedgerDay.Of(instant).ShouldBe(Day.AddDays(1));
        (instant < LedgerDay.End(Day)).ShouldBeFalse();
    }

    [Test]
    public void AnInstantWithAnOffset_IsTakenInUtc()
    {
        // Wednesday 09:30 in Auckland (+13:00) is Tuesday 20:30 UTC (§1.1, T1).
        var instant = new DateTimeOffset(2026, 3, 4, 9, 30, 0, TimeSpan.FromHours(13));

        LedgerDay.Of(instant).ShouldBe(new DateOnly(2026, 3, 3));
        (instant < LedgerDay.End(new DateOnly(2026, 3, 3))).ShouldBeTrue();
    }
}
