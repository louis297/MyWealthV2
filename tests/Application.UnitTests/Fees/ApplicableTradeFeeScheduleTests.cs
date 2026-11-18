using MyWealthV2.Application.Fees.TradeSchedules;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Fees;

/// <summary>Trade fees R7: the schedule is picked for the UTC date of the trade's booking instant.</summary>
public class ApplicableTradeFeeScheduleTests
{
    [Test]
    public void DateOf_IsTheUtcDate()
    {
        // 23:30 and 00:30 UTC fall on two dates; the offset of the instant does not matter.
        ApplicableTradeFeeSchedule.DateOf(new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.Zero))
            .ShouldBe(new DateOnly(2026, 11, 9));
        ApplicableTradeFeeSchedule.DateOf(new DateTimeOffset(2026, 11, 10, 0, 30, 0, TimeSpan.Zero))
            .ShouldBe(new DateOnly(2026, 11, 10));
        ApplicableTradeFeeSchedule.DateOf(new DateTimeOffset(2026, 11, 10, 12, 30, 0, TimeSpan.FromHours(13)))
            .ShouldBe(new DateOnly(2026, 11, 9));
    }
}
