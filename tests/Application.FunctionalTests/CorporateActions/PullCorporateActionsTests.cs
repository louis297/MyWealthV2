using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R7–R10: pulling the announcements of a market data connection after its cursor.</summary>
[Parallelizable]
public class PullCorporateActionsTests : TestBase
{
    [Test]
    public async Task APage_IsStoredReceived_WithTheCursorAndTheSuccessTime()
    {
        Feed.Add(Connection, Item(version: 1));
        Feed.Add(Connection, Item("w-CA2", kind: CorporateActionKind.Bonus, ratioNew: 1, ratioHeld: 5, currency: "usd"));

        var outcome = await PullAsync();

        (outcome.Failure, outcome.Stored).ShouldBe(((MarketDataFailure?)null, 2));
        var stored = await AnnouncementsAsync();
        stored.Select(row => row.OutsideId).ShouldBe(["w-CA1-V1", "w-CA2-V1"]);
        var first = stored[0];
        (first.Connection, first.PartyActionId, first.Version, first.PartyStatus, first.Symbol, first.Currency, first.Kind)
            .ShouldBe((Connection, "w-CA1", 1, PartyActionStatus.Announced, "ACME.SIM", "USD", (CorporateActionKind?)CorporateActionKind.Split));
        (first.RatioNew, first.RatioHeld, first.ExDate).ShouldBe((2, 1, new DateOnly(2026, 11, 24)));
        first.Status.ShouldBe(AnnouncementStatus.Received);
        first.ReceivedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        stored[1].Currency.ShouldBe("USD");
        var state = (await FeedStateAsync()).ShouldNotBeNull();
        state.Cursor.ShouldBe("2");
        state.LastSucceededAt.ShouldNotBeNull();
        state.LastFailure.ShouldBeNull();
    }

    [Test]
    public async Task TheNextPull_ReadsAfterTheCursor_AndPagesUntilThePartyHasNoMore()
    {
        Feed.PageSize = 2;
        for (var number = 1; number <= 5; number++)
        {
            Feed.Add(Connection, Item($"w-CA{number}"));
        }

        (await PullAsync()).Stored.ShouldBe(5);
        Feed.Requests.Select(request => request.Cursor).ShouldBe([null, "2", "4"]);
        Feed.Requests.ShouldAllBe(request => request.Limit == 100);

        Feed.Add(Connection, Item("w-CA6"));
        (await PullAsync()).Stored.ShouldBe(1);
        Feed.Requests.Last().Cursor.ShouldBe("5");
        (await AnnouncementsAsync()).Count.ShouldBe(6);
    }

    [Test]
    public async Task APull_ReadsAtMost20Pages()
    {
        Feed.PageSize = 1;
        for (var number = 1; number <= 25; number++)
        {
            Feed.Add(Connection, Item($"w-CA{number}"));
        }

        (await PullAsync()).Stored.ShouldBe(20);
        Feed.Requests.Count.ShouldBe(20);
        (await PullAsync()).Stored.ShouldBe(5);
    }

    [Test]
    public async Task TheSameOutsideIdTwice_IsStoredOnce()
    {
        Feed.Add(Connection, Item(id: "same"));
        Feed.Add(Connection, Item(id: "SAME", version: 2));
        await PullAsync();
        Feed.Cursor = null;
        Feed.Add(Connection, Item(id: "same", version: 3));

        await PullAsync();

        var stored = await AnnouncementsAsync();
        stored.Count.ShouldBe(1);
        stored[0].Version.ShouldBe(1);
    }

    [Test]
    public async Task AnUnknownKind_IsStoredWithNoKind()
    {
        Feed.Add(Connection, Item(kind: null));

        (await PullAsync()).Failure.ShouldBeNull();

        (await AnnouncementsAsync()).Single().Kind.ShouldBeNull();
    }

    private static IEnumerable<TestCaseData> BadItems()
    {
        yield return new TestCaseData(Item(id: "")).SetName("ABadItem_NoOutsideId");
        yield return new TestCaseData(Item(id: new string('x', 101))).SetName("ABadItem_AnOutsideIdOver100");
        yield return new TestCaseData(Item(actionId: "")).SetName("ABadItem_NoPartyActionId");
        yield return new TestCaseData(Item(version: 0)).SetName("ABadItem_Version0");
        yield return new TestCaseData(Item(status: null)).SetName("ABadItem_NoStatus");
        yield return new TestCaseData(Item(symbol: null)).SetName("ABadItem_NoSymbol");
        yield return new TestCaseData(Item(symbol: new string('S', 33))).SetName("ABadItem_ASymbolOver32");
        yield return new TestCaseData(Item(currency: "US")).SetName("ABadItem_ACurrencyNotThreeLetters");
        yield return new TestCaseData(Item(ratioNew: 0)).SetName("ABadItem_RatioNew0");
        yield return new TestCaseData(Item(ratioHeld: null)).SetName("ABadItem_NoRatioHeld");
        yield return new TestCaseData(Item() with { ExDate = null }).SetName("ABadItem_NoExDate");
    }

    [TestCaseSource(nameof(BadItems))]
    public async Task APageWithABadItem_StoresNothing_KeepsTheCursor_AndFailsWithInvalidItem(Application.CorporateActions.CorporateActionFeedItem bad)
    {
        Feed.Add(Connection, Item("w-CA1"));
        await PullAsync();
        Feed.Add(Connection, Item("w-CA2"));
        Feed.Add(Connection, bad);

        var outcome = await PullAsync();

        outcome.Failure.ShouldBe(MarketDataFailure.InvalidItem);
        (await AnnouncementsAsync()).Select(row => row.PartyActionId).ShouldBe(["w-CA1"]);
        var state = (await FeedStateAsync()).ShouldNotBeNull();
        state.Cursor.ShouldBe("1");
        state.LastFailure.ShouldBe(MarketDataFailure.InvalidItem);
        state.LastFailedAt.ShouldNotBeNull();
        state.LastSucceededAt.ShouldNotBeNull();
    }

    [Test]
    public async Task AFailedCall_KeepsTheCursor_AndRecordsTheFailure()
    {
        Feed.Add(Connection, Item("w-CA1"));
        await PullAsync();
        Feed.Add(Connection, Item("w-CA2"));
        Feed.Fail(Connection, MarketDataFailure.Unavailable);

        var outcome = await PullAsync();

        (outcome.Failure, outcome.Stored).ShouldBe(((MarketDataFailure?)MarketDataFailure.Unavailable, 0));
        (await AnnouncementsAsync()).Count.ShouldBe(1);
        var state = (await FeedStateAsync()).ShouldNotBeNull();
        (state.Cursor, state.LastFailure).ShouldBe(("1", (MarketDataFailure?)MarketDataFailure.Unavailable));

        Feed.Fail(Connection, null);
        (await PullAsync()).Stored.ShouldBe(1);
    }

    [Test]
    public async Task ACursorOver500Characters_IsABadResponse()
    {
        Feed.Add(Connection, Item("w-CA1"));
        Feed.Cursor = new string('c', 501);

        (await PullAsync()).Failure.ShouldBe(MarketDataFailure.BadResponse);

        (await AnnouncementsAsync()).ShouldBeEmpty();
        (await FeedStateAsync()).ShouldNotBeNull().Cursor.ShouldBeNull();
    }
}
