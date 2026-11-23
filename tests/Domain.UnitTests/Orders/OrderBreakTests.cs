using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Trade-settlement R27–R30, R32 (§5): a fill the broker breaks, and the order after the break.</summary>
public class OrderBreakTests
{
    private const int TenantId = 7;
    private const int ScheduleId = 9;
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly OrderFeeTerms Fees = Terms();

    [Test]
    public void BreakingAReversedFill_OfAnOpenOrder_GivesTheQuantityBack_AndTheReservationGrows()
    {
        var order = Working();
        var reservedBefore = order.ReservedCash;
        var fill = Fill(order, 4m);
        var fee = Post(order, fill);

        order.BreakFill(fill, 90, "Broken by the exchange", At.AddHours(1), FillBreakOutcome.Reversed, Fees, fee, reversalTransactionId: 500);

        (order.Status, order.FilledQuantity, order.PostedGross, order.FeeCharged).ShouldBe((OrderStatus.Working, 0m, 0m, 0m));
        order.OpenQuantity.ShouldBe(10m);
        order.ReservedCash.ShouldBe(reservedBefore);
        (fill.Status, fill.BreakOutcome, fill.ReversalTransactionId, fill.BreakReportId, fill.BrokenAt, fill.BreakReason).ShouldBe((
            OrderFillStatus.Broken, (FillBreakOutcome?)FillBreakOutcome.Reversed, (int?)500, (long?)90, (DateTimeOffset?)At.AddHours(1),
            "Broken by the exchange"));
    }

    [Test]
    public void BreakingOneOfTwoFills_LeavesTheOrderPartlyFilled_ByItsRemainingFill()
    {
        var order = Working();
        var first = Fill(order, 4m, reportId: 1);
        var firstFee = Post(order, first);
        var second = Fill(order, 3m, reportId: 2);
        Post(order, second);

        order.BreakFill(first, 90, null, At, FillBreakOutcome.Reversed, Fees, firstFee, reversalTransactionId: 500);

        (order.Status, order.FilledQuantity).ShouldBe((OrderStatus.PartlyFilled, 3m));
        order.PostedGross.ShouldBe(300.36m);
    }

    [Test]
    public void BreakingAFillOfAFilledOrder_CancelsIt_WithTradeBroken()
    {
        var order = Working();
        var fill = Fill(order, 10m);
        var fee = Post(order, fill);

        order.BreakFill(fill, 90, null, At, FillBreakOutcome.Reversed, Fees, fee, reversalTransactionId: 500);

        (order.Status, order.CloseReason, order.FilledQuantity, order.ReservedCash).ShouldBe((
            OrderStatus.Cancelled, Order.TradeBrokenReason, 0m, 0m));
        order.ClosedAt.ShouldNotBeNull();
    }

    [Test]
    public void BreakingAFillOfACancelledOrder_KeepsItCancelled()
    {
        var order = Working();
        var fill = Fill(order, 4m);
        var fee = Post(order, fill);
        order.End(OrderStatus.Cancelled, "Requested", At.AddMinutes(5), Fees);

        order.BreakFill(fill, 90, null, At, FillBreakOutcome.Reversed, Fees, fee, reversalTransactionId: 500);

        (order.Status, order.CloseReason, order.FilledQuantity).ShouldBe((OrderStatus.Cancelled, "Requested", 0m));
    }

    [Test]
    public void BreakingAPendingFill_IsNotPosted_AndChangesNoPostedTotals()
    {
        var order = Working();
        var posted = Fill(order, 2m, reportId: 1);
        Post(order, posted);
        var (gross, charged) = (order.PostedGross, order.FeeCharged);
        var pending = Fill(order, 4m, reportId: 2);

        order.BreakFill(pending, 90, null, At, FillBreakOutcome.NotPosted, Fees);

        (pending.Status, pending.BreakOutcome, pending.TransactionId).ShouldBe((
            OrderFillStatus.Broken, (FillBreakOutcome?)FillBreakOutcome.NotPosted, (int?)null));
        (order.FilledQuantity, order.PostedGross, order.FeeCharged).ShouldBe((2m, gross, charged));
        order.Status.ShouldBe(OrderStatus.PartlyFilled);
    }

    [Test]
    public void ANotAppliedBreak_KeepsThePostedTotals_AndTakesTheQuantityBack()
    {
        var order = Working();
        var fill = Fill(order, 4m);
        Post(order, fill);
        var (gross, charged) = (order.PostedGross, order.FeeCharged);

        order.BreakFill(fill, 90, null, At, FillBreakOutcome.NotApplied, Fees, holdReason: FillBreakHoldReason.LaterActivity);

        (fill.BreakOutcome, fill.BreakHoldReason, fill.TransactionId).ShouldBe((
            (FillBreakOutcome?)FillBreakOutcome.NotApplied, (FillBreakHoldReason?)FillBreakHoldReason.LaterActivity, (int?)104));
        (order.FilledQuantity, order.PostedGross, order.FeeCharged).ShouldBe((0m, gross, charged));
    }

    [Test]
    public void AFill_IsBrokenOnce_NeverAfterItSettled_AndOnlyWithTheRightOutcome()
    {
        var order = Working();
        var fill = Fill(order, 4m);
        var fee = Post(order, fill);
        order.BreakFill(fill, 90, null, At, FillBreakOutcome.Reversed, Fees, fee, reversalTransactionId: 500);
        Should.Throw<DomainException>(() => order.BreakFill(fill, 91, null, At, FillBreakOutcome.Reversed, Fees, fee, 501));

        var settledOrder = Working();
        var settled = Fill(settledOrder, 4m);
        Post(settledOrder, settled);
        settled.Settle(Day.AddDays(2), At);
        Should.Throw<DomainException>(() => settledOrder.BreakFill(settled, 90, null, At, FillBreakOutcome.Reversed, Fees, 5m, 500));

        var other = Working();
        var pending = Fill(other, 4m);
        Should.Throw<DomainException>(() => other.BreakFill(pending, 90, null, At, FillBreakOutcome.Reversed, Fees, 0m, 500));
        var postedOne = Fill(other, 2m, reportId: 2);
        Post(other, postedOne);
        Should.Throw<DomainException>(() => other.BreakFill(postedOne, 90, null, At, FillBreakOutcome.NotPosted, Fees));
        Should.Throw<DomainException>(() => other.BreakFill(postedOne, 90, null, At, FillBreakOutcome.Reversed, Fees, 5m));
        Should.Throw<DomainException>(() => other.BreakFill(postedOne, 90, null, At, FillBreakOutcome.NotApplied, Fees));
    }

    [Test]
    public void AFillOfAnotherOrder_CannotBeBrokenThroughThisOne()
    {
        var order = Working();
        var stranger = Working();
        var fill = Fill(stranger, 4m);

        Should.Throw<DomainException>(() => order.BreakFill(fill, 90, null, At, FillBreakOutcome.NotPosted, Fees));
    }

    [Test]
    public void TheBreakReason_IsCutTo140()
    {
        var order = Working();
        var fill = Fill(order, 4m);

        order.BreakFill(fill, 90, new string('r', 150), At, FillBreakOutcome.NotPosted, Fees);

        fill.BreakReason!.Length.ShouldBe(140);
    }

    [Test]
    public void TheFeeOfALaterFill_IsNeverBelow0_AfterABreak()
    {
        var order = Working();
        var first = Fill(order, 4m, reportId: 1);
        var fee = Post(order, first);
        order.BreakFill(first, 90, null, At, FillBreakOutcome.Reversed, Fees, fee, reversalTransactionId: 500);

        order.FeeOfFill(1m, Fees).ShouldBeGreaterThanOrEqualTo(0m);
    }

    private static OrderFeeTerms Terms()
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, Currency.Create("USD", "US Dollar", 2), new DateOnly(2026, 11, 1), 5m, null, [new FeeBand(0m, 0.5m)],
            new DateOnly(2026, 10, 15));
        schedule.Id = ScheduleId;
        return new OrderFeeTerms(schedule, 2);
    }

    private static Order Working()
    {
        var order = Order.Place(TenantId, 3, 11, OrderSide.Buy, 10m, "USD", 100m, Day, Fees, "simulator", null, 21, At);
        order.RecordAttempt(At.AddMinutes(1));
        order.Accept("w-O1", At.AddMinutes(1));
        return order;
    }

    private static OrderFill Fill(Order order, decimal quantity, decimal price = 100.12m, long reportId = 4) =>
        order.AddFill(reportId, quantity, price, Day, Day.AddDays(2), "CN-1", At.AddMinutes(2), Fees);

    private static decimal Post(Order order, OrderFill fill)
    {
        var gross = decimal.Round(fill.Quantity * fill.Price, 4, MidpointRounding.AwayFromZero);
        var fee = order.FeeOfFill(gross, Fees);
        fill.MarkPosted(100 + (int)fill.ExecutionReportId, -gross - fee, At.AddMinutes(3));
        order.RecordPostedFill(gross, fee, Fees);
        return fee;
    }
}
