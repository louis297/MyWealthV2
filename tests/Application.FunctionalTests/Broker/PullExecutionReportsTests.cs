using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R23, R24: the pull step stores each execution report once, with the feed's cursor, page by page.</summary>
[Parallelizable]
public class PullExecutionReportsTests : TestBase
{
    private static readonly Guid OrderId = Guid.Parse("2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11");

    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    [Test]
    public async Task APull_StoresEachReportOnce_AsReceivedAtPlatformTime_AndMovesTheCursor()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var fill = Feed.AddFill(OrderId, 4m, 100.12m, new string('n', 60));
        var cancelled = Feed.AddEnd(OrderId, ExecutionReportKind.Cancelled, new string('r', 150));
        var settled = Feed.AddSettled(OrderId, fill.Id!);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(null, 3));
        var reports = await BrokerHttp.ReportsAsync();
        reports.Select(report => report.OutsideId).ShouldBe([fill.Id, cancelled.Id, settled.Id]);
        reports.ShouldAllBe(report => report.Connection == "simulator" && report.Status == ExecutionReportStatus.Received
                                                                       && report.ReceivedAt > before && report.ProcessedAt == null);
        var first = reports[0];
        (first.Kind, first.ClientOrderId, first.BrokerOrderId, first.Quantity, first.Price, first.TradeDate, first.SettlementDate)
            .ShouldBe((ExecutionReportKind.Fill, OrderId.ToString(), "fake-O1", 4m, 100.12m, day, day.AddDays(2)));
        first.ContractNote.ShouldBe(new string('n', 50));
        (reports[1].Kind, reports[1].Reason).ShouldBe((ExecutionReportKind.Cancelled, new string('r', 140)));
        (reports[2].Kind, reports[2].FillOutsideId, reports[2].SettledDate)
            .ShouldBe((ExecutionReportKind.Settled, fill.Id, day.AddDays(2)));

        var feed = await BrokerHttp.FeedStateAsync();
        feed.ShouldNotBeNull();
        feed.Cursor.ShouldBe("3");
        feed.LastSucceededAt!.Value.ShouldBeGreaterThan(before);
        (feed.LastFailedAt, feed.LastFailure).ShouldBe(((DateTimeOffset?)null, (BrokerFailure?)null));
        Feed.Requests.ShouldBe([("simulator", null, 100)]);
    }

    [Test]
    public async Task ASecondPull_AsksAfterTheCursor_AndStoresOnlyWhatIsNew()
    {
        Feed.AddFill(OrderId, 4m, 100.12m);
        await BrokerHttp.PullAsync();
        Feed.AddFill(OrderId, 6m, 100.17m, "CN-2");

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(null, 1));
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(2);
        Feed.Requests.Select(request => request.Cursor).ShouldBe([null, "1"]);
        (await BrokerHttp.FeedStateAsync())!.Cursor.ShouldBe("2");
    }

    [Test]
    public async Task TheSameOutsideId_Twice_IsStoredOnce_AndNotChanged()
    {
        var fill = Feed.AddFill(OrderId, 4m, 100.12m);
        Feed.Add(fill with { Quantity = 9m });
        await BrokerHttp.PullAsync();
        Feed.Add(fill with { Id = fill.Id!.ToUpperInvariant(), Price = 1m });

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(null, 0));
        var report = (await BrokerHttp.ReportsAsync()).ShouldHaveSingleItem();
        (report.Quantity, report.Price).ShouldBe((4m, 100.12m));
        (await BrokerHttp.FeedStateAsync())!.Cursor.ShouldBe("3");
    }

    [Test]
    public async Task APull_ReadsPageByPage_UntilThePartySaysThereAreNoMore()
    {
        for (var index = 0; index < 5; index++)
        {
            Feed.AddFill(OrderId, 1m, 100m);
        }

        Feed.PageSize = 2;

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(null, 5));
        Feed.Requests.ShouldBe([("simulator", null, 100), ("simulator", "2", 100), ("simulator", "4", 100)]);
        (await BrokerHttp.FeedStateAsync())!.Cursor.ShouldBe("5");
    }

    [Test]
    public async Task APull_StopsAfter20Pages_AndTheNextPullGoesOn()
    {
        for (var index = 0; index < 22; index++)
        {
            Feed.AddFill(OrderId, 1m, 100m);
        }

        Feed.PageSize = 1;

        (await BrokerHttp.PullAsync()).ShouldBe(new BrokerPullOutcome(null, 20));
        Feed.Requests.Count.ShouldBe(20);
        (await BrokerHttp.PullAsync()).ShouldBe(new BrokerPullOutcome(null, 2));
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(22);
    }

    private static IEnumerable<TestCaseData> BadItems()
    {
        var day = new DateOnly(2026, 11, 23);
        var fill = new BrokerFeedItem(
            "bad-X1", ExecutionReportKind.Fill, OrderId.ToString(), "fake-O1", 4m, 100m, day, day.AddDays(2), "CN-1", null, null, null);
        yield return new TestCaseData(fill with { Id = null }).SetName("BadItem(no outside id)");
        yield return new TestCaseData(fill with { Id = new string('x', 101) }).SetName("BadItem(outside id over 100)");
        yield return new TestCaseData(fill with { ClientOrderId = null }).SetName("BadItem(no client order id)");
        yield return new TestCaseData(fill with { Quantity = 0m }).SetName("BadItem(quantity 0)");
        yield return new TestCaseData(fill with { Quantity = null }).SetName("BadItem(quantity not a number)");
        yield return new TestCaseData(fill with { Price = 0.123456789m }).SetName("BadItem(price with 9 decimals)");
        yield return new TestCaseData(fill with { TradeDate = null }).SetName("BadItem(no trade date)");
        yield return new TestCaseData(fill with { SettlementDate = day.AddDays(-1) }).SetName("BadItem(settles before it trades)");
        yield return new TestCaseData(new BrokerFeedItem(
                "bad-X1", ExecutionReportKind.Settled, OrderId.ToString(), null, null, null, null, null, null, null, day, null))
            .SetName("BadItem(settled without a fill id)");
        yield return new TestCaseData(new BrokerFeedItem(
                "bad-X1", ExecutionReportKind.Settled, OrderId.ToString(), null, null, null, null, null, null, "fake-X1", null, null))
            .SetName("BadItem(settled without a date)");
    }

    [TestCaseSource(nameof(BadItems))]
    public async Task APageWithABadItem_StoresNothingOfThatPage_KeepsTheCursor_AndSetsInvalidItem(BrokerFeedItem bad)
    {
        Feed.AddFill(OrderId, 1m, 100m);
        Feed.AddFill(OrderId, 1m, 100m);
        Feed.AddFill(OrderId, 1m, 100m);
        Feed.Add(bad);
        Feed.PageSize = 2;
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await BrokerHttp.PullAsync();

        // The first page is stored; the second holds a good item and the bad one, and is refused whole.
        outcome.ShouldBe(new BrokerPullOutcome(BrokerFailure.InvalidItem, 2));
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(2);
        var feed = (await BrokerHttp.FeedStateAsync())!;
        feed.Cursor.ShouldBe("2");
        feed.LastFailure.ShouldBe(BrokerFailure.InvalidItem);
        feed.LastFailedAt!.Value.ShouldBeGreaterThan(before);
        feed.LastSucceededAt!.Value.ShouldBeGreaterThan(before);
    }

    [Test]
    public async Task AReportOfAKindTheAdapterDoesNotKnow_IsStoredWithNoKind()
    {
        Feed.Add(new BrokerFeedItem("fake-X9", null, null, null, null, null, null, null, null, null, null, "Something new"));

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(null, 1));
        var report = (await BrokerHttp.ReportsAsync()).ShouldHaveSingleItem();
        (report.Kind, report.Reason, report.Status).ShouldBe(((ExecutionReportKind?)null, "Something new", ExecutionReportStatus.Received));
    }

    [TestCase(BrokerFailure.Unreachable)]
    [TestCase(BrokerFailure.Timeout)]
    [TestCase(BrokerFailure.Unauthorized)]
    [TestCase(BrokerFailure.Unavailable)]
    [TestCase(BrokerFailure.BadResponse)]
    public async Task AFailedPull_StoresNothing_KeepsTheCursor_AndRecordsTheFailure(BrokerFailure failure)
    {
        Feed.AddFill(OrderId, 1m, 100m);
        await BrokerHttp.PullAsync();
        Feed.AddFill(OrderId, 1m, 100m);
        Feed.Fail(failure);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(failure, 0));
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(1);
        var feed = (await BrokerHttp.FeedStateAsync())!;
        (feed.Cursor, feed.LastFailure).ShouldBe(("1", (BrokerFailure?)failure));
        feed.LastFailedAt!.Value.ShouldBeGreaterThan(before);

        Feed.Fail(null);
        (await BrokerHttp.PullAsync()).ShouldBe(new BrokerPullOutcome(null, 1));
        (await BrokerHttp.FeedStateAsync())!.Cursor.ShouldBe("2");
    }

    [Test]
    public async Task ACursorOver500Characters_IsABadResponse_AndNothingIsStored()
    {
        Feed.AddFill(OrderId, 1m, 100m);
        Feed.Cursor = new string('c', 501);

        var outcome = await BrokerHttp.PullAsync();

        outcome.ShouldBe(new BrokerPullOutcome(BrokerFailure.BadResponse, 0));
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
        (await BrokerHttp.FeedStateAsync())!.Cursor.ShouldBeNull();
    }

    [Test]
    public async Task AConnectionThatIsNotConfigured_ReadsNothing()
    {
        Feed.AddFill(OrderId, 1m, 100m, connection: "other");

        var outcome = await BrokerHttp.PullAsync("other");

        outcome.ShouldBe(new BrokerPullOutcome(null, 0));
        Feed.Requests.ShouldBeEmpty();
        (await BrokerHttp.ReportsAsync("other")).ShouldBeEmpty();
    }
}
