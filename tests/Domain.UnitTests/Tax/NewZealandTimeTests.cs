using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>
/// Fif-report R13: New Zealand is 13 hours ahead of UTC from 14:00 UTC on the Saturday before the last Sunday of
/// September until 14:00 UTC on the Saturday before the first Sunday of April, and 12 hours ahead otherwise.
/// </summary>
public class NewZealandTimeTests
{
    private static readonly TimeSpan Nzdt = TimeSpan.FromHours(13);
    private static readonly TimeSpan Nzst = TimeSpan.FromHours(12);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    [TestCase(2027, 4, 3)]
    [TestCase(2028, 4, 1)]
    public void DaylightTimeEnds_At1400UtcOnTheSaturdayBeforeTheFirstSundayOfApril(int year, int month, int day)
    {
        var change = Utc(year, month, day, 14);

        NewZealandTime.Offset(change.AddSeconds(-1)).ShouldBe(Nzdt);
        NewZealandTime.Offset(change).ShouldBe(Nzst);
    }

    [TestCase(2026, 9, 26)]
    [TestCase(2027, 9, 25)]
    public void DaylightTimeStarts_At1400UtcOnTheSaturdayBeforeTheLastSundayOfSeptember(int year, int month, int day)
    {
        var change = Utc(year, month, day, 14);

        NewZealandTime.Offset(change.AddSeconds(-1)).ShouldBe(Nzst);
        NewZealandTime.Offset(change).ShouldBe(Nzdt);
    }

    [Test]
    public void TheChanges_Are0200To0300InSeptemberAnd0300To0200InApril()
    {
        NewZealandTime.Local(Utc(2026, 9, 26, 13, 59)).ShouldBe(new DateTime(2026, 9, 27, 1, 59, 0));
        NewZealandTime.Local(Utc(2026, 9, 26, 14)).ShouldBe(new DateTime(2026, 9, 27, 3, 0, 0));
        NewZealandTime.Local(Utc(2027, 4, 3, 13, 59)).ShouldBe(new DateTime(2027, 4, 4, 2, 59, 0));
        NewZealandTime.Local(Utc(2027, 4, 3, 14)).ShouldBe(new DateTime(2027, 4, 4, 2, 0, 0));
    }

    [TestCase(2009)]
    [TestCase(2019)] // 1 April 2018 is a Sunday: daylight time ends that morning, after midnight.
    [TestCase(2026)]
    [TestCase(2027)]
    [TestCase(2030)] // 1 April 2029 is a Sunday.
    public void TheStartOfATaxYear_IsMidnightOn1April(int taxYear)
    {
        NewZealandTime.Local(TaxYear.Start(taxYear)).ShouldBe(new DateTime(taxYear - 1, 4, 1, 0, 0, 0));
    }

    [Test]
    public void TheTimesOfTheWorkedExample()
    {
        NewZealandTime.Local(Utc(2025, 3, 31, 20, 15)).ShouldBe(new DateTime(2025, 4, 1, 9, 15, 0));
        NewZealandTime.Local(Utc(2025, 7, 2, 12, 1)).ShouldBe(new DateTime(2025, 7, 3, 0, 1, 0));
        NewZealandTime.Local(Utc(2025, 11, 14, 3, 20)).ShouldBe(new DateTime(2025, 11, 14, 16, 20, 0));
        NewZealandTime.Local(Utc(2026, 2, 2, 21, 40)).ShouldBe(new DateTime(2026, 2, 3, 10, 40, 0));
    }

    [Test]
    public void AnInstantWithAnOffset_IsReadAsItsUtcInstant()
    {
        var instant = new DateTimeOffset(2025, 4, 1, 9, 15, 0, TimeSpan.FromHours(13));

        NewZealandTime.Local(instant).ShouldBe(new DateTime(2025, 4, 1, 9, 15, 0));
    }

    [Test]
    public void BeforeTheRuleOf2007_ThereIsNoOffset()
    {
        var first = Utc(2007, 9, 29, 14);

        NewZealandTime.Offset(first.AddSeconds(-1)).ShouldBeNull();
        NewZealandTime.Local(first.AddSeconds(-1)).ShouldBeNull();
        NewZealandTime.Offset(first).ShouldBe(Nzdt);
        NewZealandTime.Local(first).ShouldBe(new DateTime(2007, 9, 30, 3, 0, 0));
    }

    [Test]
    public void TheOffset_IsTwelveOrThirteenHoursThroughAYear()
    {
        for (var instant = Utc(2026, 1, 1, 0); instant < Utc(2027, 1, 1, 0); instant = instant.AddHours(1))
        {
            NewZealandTime.Offset(instant).ShouldBeOneOf(Nzst, Nzdt);
        }
    }
}
