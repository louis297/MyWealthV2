using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Orders R23–R25 (docs/features/orders.md §5): the execution report and the broker feed state.</summary>
public class ExecutionReportTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 11, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly DateOnly Settles = new(2026, 11, 25);
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";

    private static bool Fill(
        string? outsideId = "w-X1",
        string? clientOrderId = OrderId,
        decimal? quantity = 4m,
        decimal? price = 100.12m,
        DateOnly? tradeDate = null,
        DateOnly? settlementDate = null,
        bool noTradeDate = false,
        bool noSettlementDate = false) =>
        ExecutionReport.IsStorable(
            outsideId, ExecutionReportKind.Fill, clientOrderId, quantity, price,
            noTradeDate ? null : tradeDate ?? Day, noSettlementDate ? null : settlementDate ?? Settles, null, null);

    [Test]
    public void AFill_WithEveryField_IsStorable() => Fill().ShouldBeTrue();

    [Test]
    public void AFill_ThatSettlesOnItsTradeDate_IsStorable() => Fill(settlementDate: Day).ShouldBeTrue();

    [Test]
    public void AnItem_NeedsAnOutsideIdOf1To100()
    {
        Fill(outsideId: null).ShouldBeFalse();
        Fill(outsideId: "").ShouldBeFalse();
        Fill(outsideId: new string('x', 101)).ShouldBeFalse();
        Fill(outsideId: new string('x', 100)).ShouldBeTrue();
    }

    [Test]
    public void AnItemOfAKnownKind_NeedsAClientOrderIdOf1To100()
    {
        Fill(clientOrderId: null).ShouldBeFalse();
        Fill(clientOrderId: "").ShouldBeFalse();
        Fill(clientOrderId: new string('x', 101)).ShouldBeFalse();
        Fill(clientOrderId: "not-a-guid").ShouldBeTrue();
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(0.123456789)]
    [TestCase(10000000000.0)]
    public void AFill_NeedsAQuantityAboveZero_WithAtMost8Decimals_ThatFitsTheColumn(decimal? quantity) =>
        Fill(quantity: quantity).ShouldBeFalse();

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(0.123456789)]
    [TestCase(10000000000.0)]
    public void AFill_NeedsAPriceAboveZero_WithAtMost8Decimals_ThatFitsTheColumn(decimal? price) =>
        Fill(price: price).ShouldBeFalse();

    [Test]
    public void AFill_NeedsATradeDate_AndASettlementDateNotBeforeIt()
    {
        Fill(noTradeDate: true).ShouldBeFalse();
        Fill(noSettlementDate: true).ShouldBeFalse();
        Fill(settlementDate: Day.AddDays(-1)).ShouldBeFalse();
    }

    [TestCase(ExecutionReportKind.Cancelled)]
    [TestCase(ExecutionReportKind.Expired)]
    public void ACancelledOrExpiredItem_NeedsOnlyItsIds(ExecutionReportKind kind)
    {
        ExecutionReport.IsStorable("w-X2", kind, OrderId, null, null, null, null, null, null).ShouldBeTrue();
        ExecutionReport.IsStorable("w-X2", kind, null, null, null, null, null, null, null).ShouldBeFalse();
    }

    [Test]
    public void ASettledItem_NeedsTheFillsOutsideId_AndASettledDate()
    {
        ExecutionReport.IsStorable("w-X3", ExecutionReportKind.Settled, OrderId, null, null, null, null, "w-X1", Settles).ShouldBeTrue();
        ExecutionReport.IsStorable("w-X3", ExecutionReportKind.Settled, OrderId, null, null, null, null, null, Settles).ShouldBeFalse();
        ExecutionReport.IsStorable("w-X3", ExecutionReportKind.Settled, OrderId, null, null, null, null, "", Settles).ShouldBeFalse();
        ExecutionReport.IsStorable("w-X3", ExecutionReportKind.Settled, OrderId, null, null, null, null, new string('x', 101), Settles).ShouldBeFalse();
        ExecutionReport.IsStorable("w-X3", ExecutionReportKind.Settled, OrderId, null, null, null, null, "w-X1", null).ShouldBeFalse();
    }

    [Test]
    public void AnItemWithNoKind_NeedsOnlyAnOutsideId()
    {
        ExecutionReport.IsStorable("w-X4", null, null, null, null, null, null, null, null).ShouldBeTrue();
        ExecutionReport.IsStorable(null, null, OrderId, 1m, 1m, Day, Day, null, null).ShouldBeFalse();
    }

    [Test]
    public void Receive_IsReceivedAtPlatformTime_AndKeepsWhatThePartySent()
    {
        var report = ExecutionReport.Receive(
            "simulator", "w-X1", ExecutionReportKind.Fill, OrderId, "w-O1", 4m, 100.12m, Day, Settles, new string('n', 60),
            null, null, null, At.ToOffset(TimeSpan.FromHours(13)));

        report.PublicId.ShouldNotBe(Guid.Empty);
        report.Connection.ShouldBe("simulator");
        report.OutsideId.ShouldBe("w-X1");
        report.Kind.ShouldBe(ExecutionReportKind.Fill);
        report.ClientOrderId.ShouldBe(OrderId);
        report.BrokerOrderId.ShouldBe("w-O1");
        report.Quantity.ShouldBe(4m);
        report.Price.ShouldBe(100.12m);
        report.TradeDate.ShouldBe(Day);
        report.SettlementDate.ShouldBe(Settles);
        report.ContractNote.ShouldBe(new string('n', 50));
        report.ReceivedAt.ShouldBe(At);
        report.ReceivedAt.Offset.ShouldBe(TimeSpan.Zero);
        report.Status.ShouldBe(ExecutionReportStatus.Received);
        report.SkipReason.ShouldBeNull();
        report.ProcessedAt.ShouldBeNull();
    }

    [Test]
    public void Receive_CutsTheReasonTo140()
    {
        var report = ExecutionReport.Receive(
            "simulator", "w-X2", ExecutionReportKind.Cancelled, OrderId, null, null, null, null, null, null, null, null,
            new string('r', 150), At);

        report.Reason.ShouldBe(new string('r', 140));
    }

    [Test]
    public void Receive_WithNoKind_KeepsOnlyWhatFitsItsColumns()
    {
        var report = ExecutionReport.Receive(
            "simulator", "w-X9", null, new string('c', 120), new string('b', 120), 0.123456789m, 20000000000m, Day, null,
            null, new string('f', 120), null, null, At);

        report.Kind.ShouldBeNull();
        report.ClientOrderId.ShouldBe(new string('c', 100));
        report.BrokerOrderId.ShouldBe(new string('b', 100));
        report.FillOutsideId.ShouldBe(new string('f', 100));
        report.Quantity.ShouldBeNull();
        report.Price.ShouldBeNull();
        report.TradeDate.ShouldBe(Day);
    }

    [Test]
    public void Receive_RefusesAnItemThatIsNotStorable() =>
        Should.Throw<DomainException>(() => ExecutionReport.Receive(
            "simulator", "w-X1", ExecutionReportKind.Fill, OrderId, null, 0m, 100m, Day, Settles, null, null, null, null, At));

    [Test]
    public void MarkProcessed_AndSkip_StartFromReceivedOnly()
    {
        var processed = Cancelled();
        processed.MarkProcessed(At.AddSeconds(1));
        processed.Status.ShouldBe(ExecutionReportStatus.Processed);
        processed.ProcessedAt.ShouldBe(At.AddSeconds(1));
        processed.SkipReason.ShouldBeNull();
        Should.Throw<DomainException>(() => processed.Skip(ExecutionReportSkipReason.Repeated, At));

        var skipped = Cancelled();
        skipped.Skip(ExecutionReportSkipReason.UnknownOrder, At.AddSeconds(2));
        skipped.Status.ShouldBe(ExecutionReportStatus.Skipped);
        skipped.SkipReason.ShouldBe(ExecutionReportSkipReason.UnknownOrder);
        skipped.ProcessedAt.ShouldBe(At.AddSeconds(2));
        Should.Throw<DomainException>(() => skipped.MarkProcessed(At));
    }

    [Test]
    public void ABrokerFeed_StartsWithNothingTried()
    {
        var feed = BrokerFeed.Start("simulator");

        feed.Connection.ShouldBe("simulator");
        feed.Cursor.ShouldBeNull();
        feed.LastSucceededAt.ShouldBeNull();
        feed.LastFailedAt.ShouldBeNull();
        feed.LastFailure.ShouldBeNull();
    }

    [Test]
    public void Advance_MovesTheCursor_AndKeepsItWhenThePartySendsNone()
    {
        var feed = BrokerFeed.Start("simulator");
        feed.RecordFailure(BrokerFailure.Unavailable, At);

        feed.Advance("w:3", At.AddMinutes(1));
        feed.Advance(null, At.AddMinutes(2));

        feed.Cursor.ShouldBe("w:3");
        feed.LastSucceededAt.ShouldBe(At.AddMinutes(2));
        feed.LastFailedAt.ShouldBe(At);
        feed.LastFailure.ShouldBe(BrokerFailure.Unavailable);
    }

    [Test]
    public void Advance_RefusesACursorOver500() =>
        Should.Throw<DomainException>(() => BrokerFeed.Start("simulator").Advance(new string('c', 501), At));

    [Test]
    public void RecordFailure_KeepsTheCursorAndTheLastSuccess()
    {
        var feed = BrokerFeed.Start("simulator");
        feed.Advance("w:3", At);

        feed.RecordFailure(BrokerFailure.InvalidItem, At.AddMinutes(1));

        feed.Cursor.ShouldBe("w:3");
        feed.LastSucceededAt.ShouldBe(At);
        feed.LastFailedAt.ShouldBe(At.AddMinutes(1));
        feed.LastFailure.ShouldBe(BrokerFailure.InvalidItem);
    }

    private static ExecutionReport Cancelled() =>
        ExecutionReport.Receive(
            "simulator", "w-X2", ExecutionReportKind.Cancelled, OrderId, "w-O1", null, null, null, null, null, null, null,
            "Requested", At);
}
