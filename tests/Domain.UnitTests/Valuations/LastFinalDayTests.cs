using MyWealthV2.Domain.MarketData;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Valuation-run R4: the last final day is today minus two days.</summary>
public class LastFinalDayTests
{
    [TestCase("2026-03-13", "2026-03-11")]
    [TestCase("2026-03-01", "2026-02-27")]
    [TestCase("2027-01-01", "2026-12-30")]
    public void LastFinalDay_IsTodayMinusTwoDays(string today, string lastFinal)
    {
        ValuationRules.LastFinalDay(DateOnly.Parse(today)).ShouldBe(DateOnly.Parse(lastFinal));
    }

    [Test]
    public void LastFinalDay_IsTheLatestDayWhoseRowsAreFinal()
    {
        var today = new DateOnly(2026, 3, 13);
        var lastFinal = ValuationRules.LastFinalDay(today);

        MarketDataRow.IsFinal(lastFinal, today).ShouldBeTrue();
        MarketDataRow.IsFinal(lastFinal.AddDays(1), today).ShouldBeFalse();
    }
}
