using MyWealthV2.Application.Broker;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R25–R29: the processing step turns a stored execution report into a change of its order, or skips
/// it with a reason.</summary>
[Parallelizable]
public class ProcessExecutionReportsTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    /// <summary>A buy of 10 the broker accepted.</summary>
    private static async Task<(OrderWorld World, Guid OrderId)> WorkingAsync(string side = "Buy", decimal quantity = 10m)
    {
        var world = await OrderWorld.CreateAsync();
        if (side == "Sell")
        {
            await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        }

        var id = await world.PlaceAsync(side, quantity);
        await BrokerHttp.SendAsync();
        return (world, id);
    }

    [Test]
    public async Task AFillReport_AddsOneFillToItsOrder_WithWhatTheBrokerSaid_AndIsProcessed()
    {
        var (world, id) = await WorkingAsync();
        var day = OrderHttp.Today;
        var sent = Feed.AddFill(id, 4m, 100.12m, "CN-1");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await BrokerHttp.PullAsync();
        var outcome = await BrokerHttp.ProcessAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 0, 0));
        var report = await BrokerHttp.ReportAsync(sent.Id!);
        (report.Status, report.SkipReason).ShouldBe((ExecutionReportStatus.Processed, (ExecutionReportSkipReason?)null));
        report.ProcessedAt!.Value.ShouldBeGreaterThan(before);
        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.FilledQuantity, order.ClosedAt).ShouldBe((OrderStatus.PartlyFilled, 4m, (DateTimeOffset?)null));
        var fill = order.Fills.ShouldHaveSingleItem();
        (fill.ExecutionReportId, fill.Quantity, fill.Price, fill.TradeDate, fill.SettlementDate, fill.BrokerReference)
            .ShouldBe((report.Id, 4m, 100.12m, day, day.AddDays(2), "CN-1"));
        (fill.TenantId, fill.SettledAt).ShouldBe((world.Tenant.Id, (DateTimeOffset?)null));

        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("PartlyFilled");
        item.GetProperty("filledQuantity").GetDecimal().ShouldBe(4m);
        item.GetProperty("openQuantity").GetDecimal().ShouldBe(6m);
        item.GetProperty("averagePrice").GetDecimal().ShouldBe(100.12m);
        var listed = item.GetProperty("fills").EnumerateArray().Single();
        listed.GetProperty("id").GetGuid().ShouldBe(fill.PublicId);
        listed.GetProperty("quantity").GetDecimal().ShouldBe(4m);
        listed.GetProperty("price").GetDecimal().ShouldBe(100.12m);
        listed.GetProperty("tradeDate").GetString().ShouldBe(day.ToString("yyyy-MM-dd"));
        listed.GetProperty("settlementDate").GetString().ShouldBe(day.AddDays(2).ToString("yyyy-MM-dd"));
        listed.GetProperty("brokerReference").GetString().ShouldBe("CN-1");
    }

    [Test]
    public async Task TwoFills_MakeTheOrderPartlyFilled_ThenFilled_WithTheWeightedAveragePrice()
    {
        var (world, id) = await WorkingAsync();
        Feed.AddFill(id, 4m, 100.12m, "CN-1");
        await BrokerHttp.RunReportsAsync();
        (await OrderHttp.OrderAsync(id)).Status.ShouldBe(OrderStatus.PartlyFilled);
        Feed.AddFill(id, 6m, 100.17m, "CN-2");
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await BrokerHttp.RunReportsAsync();

        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Filled");
        item.GetProperty("filledQuantity").GetDecimal().ShouldBe(10m);
        item.GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        item.GetProperty("averagePrice").GetDecimal().ShouldBe(100.15m);
        item.GetProperty("closedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("fills").EnumerateArray().Select(fill => fill.GetProperty("brokerReference").GetString()).ShouldBe(["CN-1", "CN-2"]);
    }

    [Test]
    public async Task AFill_BeforeTheAcceptedAnswerWasStored_MovesAPlacedOrderOn()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        BrokerHttp.Orders.Answer(BrokerPlaceResult.Failed(BrokerFailure.Timeout));
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 4m, 100.12m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.FilledQuantity, order.BrokerOrderId).ShouldBe((OrderStatus.PartlyFilled, 4m, (string?)null));
    }

    [TestCase(ExecutionReportKind.Cancelled, "Requested", OrderStatus.Cancelled)]
    [TestCase(ExecutionReportKind.Expired, null, OrderStatus.Expired)]
    public async Task ACancelledOrExpiredReport_EndsAnOpenOrder_WithTheBrokersReason_AndReleasesWhatWasNotFilled(
        ExecutionReportKind kind,
        string? reason,
        OrderStatus status)
    {
        var (world, id) = await WorkingAsync();
        var sent = Feed.AddEnd(id, kind, reason);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await BrokerHttp.RunReportsAsync();

        (await BrokerHttp.ReportAsync(sent.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe(status.ToString());
        item.GetProperty("closedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("closeReason").GetString().ShouldBe(reason);
        item.GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        (await world.CashAsync()).ShouldBe((5000.00m, 0m, 5000.00m));
    }

    [Test]
    public async Task ACancelledReport_EndsASellOrder_AndReleasesItsQuantity()
    {
        var (world, id) = await WorkingAsync("Sell", 30m);
        Feed.AddEnd(id, ExecutionReportKind.Cancelled, "Requested");

        await BrokerHttp.RunReportsAsync();

        (await OrderHttp.OrderAsync(id)).ReservedQuantity.ShouldBe(0m);
        (await world.TryPlaceAsync("Sell", 40m)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
    }

    [Test]
    public async Task AFill_ThatCrossedACancellation_IsAddedToTheCancelledOrder()
    {
        var (_, id) = await WorkingAsync();
        Feed.AddEnd(id, ExecutionReportKind.Cancelled, "Requested");
        var late = Feed.AddFill(id, 4m, 100.12m);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped).ShouldBe((2, 0));
        (await BrokerHttp.ReportAsync(late.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.FilledQuantity, order.Fills.Count).ShouldBe((OrderStatus.Cancelled, 4m, 1));
    }

    [Test]
    public async Task AReportOfAnUnknownKind_IsSkippedAsUnsupportedKind()
    {
        var (_, id) = await WorkingAsync();
        var sent = Feed.Add(new BrokerFeedItem("fake-X99", null, id.ToString(), null, null, null, null, null, null, null, null, "New"));

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((0, 1, 0));
        var report = await BrokerHttp.ReportAsync(sent.Id!);
        (report.Status, report.SkipReason).ShouldBe((ExecutionReportStatus.Skipped, (ExecutionReportSkipReason?)ExecutionReportSkipReason.UnsupportedKind));
        report.ProcessedAt.ShouldNotBeNull();
        (await OrderHttp.OrderAsync(id)).Status.ShouldBe(OrderStatus.Working);
    }

    [Test]
    public async Task AReport_WhoseClientOrderIdIsNotAGuid_OrNamesNoOrderOfTheConnection_IsSkippedAsUnknownOrder()
    {
        var (_, id) = await WorkingAsync();
        var notAGuid = Feed.Add(new BrokerFeedItem(
            Feed.NextId(), ExecutionReportKind.Cancelled, "ORDER-17", null, null, null, null, null, null, null, null, null));
        var noOrder = Feed.AddFill(Guid.NewGuid(), 1m, 100m);
        var otherConnection = Feed.AddFill(id, 1m, 100m, connection: "other");
        await OrderHttp.SetConnectionAsync(id, "other");
        var nowElsewhere = Feed.AddFill(id, 1m, 100m);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped).ShouldBe((0, 3));
        foreach (var sent in new[] { notAGuid, noOrder, nowElsewhere })
        {
            var report = await BrokerHttp.ReportAsync(sent.Id!);
            (report.Status, report.SkipReason)
                .ShouldBe((ExecutionReportStatus.Skipped, (ExecutionReportSkipReason?)ExecutionReportSkipReason.UnknownOrder));
        }

        (await BrokerHttp.ReportsAsync("other")).ShouldBeEmpty();
        otherConnection.Id.ShouldNotBeNull();
        (await OrderHttp.OrderAsync(id)).FilledQuantity.ShouldBe(0m);
    }

    [Test]
    public async Task AFill_OfMoreThanIsLeftOfTheOrder_IsSkippedAsOverfill()
    {
        var (_, id) = await WorkingAsync();
        Feed.AddFill(id, 4m, 100.12m);
        var tooMuch = Feed.AddFill(id, 6.00000001m, 100.17m);
        var exact = Feed.AddFill(id, 6m, 100.17m);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped).ShouldBe((2, 1));
        (await BrokerHttp.ReportAsync(tooMuch.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.Overfill);
        (await BrokerHttp.ReportAsync(exact.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.FilledQuantity, order.Fills.Count).ShouldBe((OrderStatus.Filled, 10m, 2));
    }

    [Test]
    public async Task ASecondCancelled_OrAnExpiredForAFilledOrder_IsSkippedAsRepeated()
    {
        var (_, cancelled) = await WorkingAsync();
        Feed.AddEnd(cancelled, ExecutionReportKind.Cancelled, "Requested");
        var again = Feed.AddEnd(cancelled, ExecutionReportKind.Cancelled, "Requested");
        var expired = Feed.AddEnd(cancelled, ExecutionReportKind.Expired);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped).ShouldBe((1, 2));
        (await BrokerHttp.ReportAsync(again.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.Repeated);
        (await BrokerHttp.ReportAsync(expired.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.Repeated);
        var order = await OrderHttp.OrderAsync(cancelled);
        (order.Status, order.CloseReason).ShouldBe((OrderStatus.Cancelled, "Requested"));
    }

    [Test]
    public async Task TheBooksFollowTheBroker_ForADisabledTenantAndADisabledCustomer()
    {
        var (world, id) = await WorkingAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        await TenantDisabler.DisableAsync(world.Tenant.Id);
        Feed.AddFill(id, 4m, 100.12m);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 0, 0));
        (await OrderHttp.OrderAsync(id)).FilledQuantity.ShouldBe(4m);
    }

    [Test]
    public async Task AProcessedReport_IsNotProcessedAgain_AndAConnectionThatIsNotConfiguredProcessesNothing()
    {
        var (_, id) = await WorkingAsync();
        Feed.AddFill(id, 4m, 100.12m);
        await BrokerHttp.RunReportsAsync();

        var again = await BrokerHttp.ProcessAsync();
        var other = await BrokerHttp.ProcessAsync("other");

        (again.Processed, again.Skipped, again.Failed).ShouldBe((0, 0, 0));
        (other.Processed, other.Skipped, other.Failed).ShouldBe((0, 0, 0));
        (await OrderHttp.OrderAsync(id)).Fills.Count.ShouldBe(1);
    }
}
