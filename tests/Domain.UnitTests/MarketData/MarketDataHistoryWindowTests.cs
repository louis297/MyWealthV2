using MyWealthV2.Domain.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.MarketData;

/// <summary>Market data history R2, R3: the first date a source is asked for.</summary>
public class MarketDataHistoryWindowTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Test]
    public void NoDays_StartsAtTheFloorDate()
    {
        var window = new MarketDataHistoryWindow(new DateOnly(2020, 1, 1), Today);

        window.From.ShouldBe(new DateOnly(2020, 1, 1));
        window.Today.ShouldBe(Today);
    }

    [Test]
    public void FloorLaterThanTodayMinusDays_StartsAtTheFloorDate()
    {
        var window = new MarketDataHistoryWindow(new DateOnly(2026, 1, 1), Today, 360);

        window.From.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Test]
    public void FloorEarlierThanTodayMinusDays_StartsAtTodayMinusDays()
    {
        var window = new MarketDataHistoryWindow(new DateOnly(2020, 1, 1), Today, 360);

        window.From.ShouldBe(Today.AddDays(-360));
    }

    [Test]
    public void Contains_BothEdges_AndNothingOutside()
    {
        var window = new MarketDataHistoryWindow(new DateOnly(2026, 1, 1), Today, 360);

        window.Contains(new DateOnly(2026, 1, 1)).ShouldBeTrue();
        window.Contains(Today).ShouldBeTrue();
        window.Contains(new DateOnly(2026, 6, 1)).ShouldBeTrue();
        window.Contains(new DateOnly(2025, 12, 31)).ShouldBeFalse();
        window.Contains(Today.AddDays(1)).ShouldBeFalse();
    }
}
