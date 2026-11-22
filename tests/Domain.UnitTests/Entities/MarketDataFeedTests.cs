using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Simulated instruments R5, R8, R9: the state of one feed of a market data connection.</summary>
public class MarketDataFeedTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 15, 1, TimeSpan.Zero);

    [Test]
    public void Start_HasNothingTriedYet()
    {
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.Prices);

        feed.Connection.ShouldBe("simulator");
        feed.Kind.ShouldBe(MarketDataFeedKind.Prices);
        feed.Cursor.ShouldBeNull();
        feed.LastSucceededAt.ShouldBeNull();
        feed.LastFailedAt.ShouldBeNull();
        feed.LastFailure.ShouldBeNull();
    }

    [Test]
    public void Succeeded_SetsTheSuccessTime_AndKeepsTheLastFailure()
    {
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.Prices);
        feed.Failed(MarketDataFailure.Unavailable, At);

        feed.Succeeded(At.AddMinutes(1));

        feed.LastSucceededAt.ShouldBe(At.AddMinutes(1));
        feed.LastFailedAt.ShouldBe(At);
        feed.LastFailure.ShouldBe(MarketDataFailure.Unavailable);
    }

    [Test]
    public void Failed_SetsTheFailureAndItsTime_AndKeepsTheLastSuccess()
    {
        var feed = MarketDataFeed.Start("simulator", MarketDataFeedKind.Prices);
        feed.Succeeded(At);

        feed.Failed(MarketDataFailure.Timeout, At.AddMinutes(1));

        feed.LastSucceededAt.ShouldBe(At);
        feed.LastFailedAt.ShouldBe(At.AddMinutes(1));
        feed.LastFailure.ShouldBe(MarketDataFailure.Timeout);
    }
}
