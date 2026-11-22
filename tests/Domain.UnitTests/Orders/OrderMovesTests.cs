using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Orders R14, R18, R21, R22, R26–R28, R33, R39, R40 (docs/features/orders.md §5): the moves of an order.</summary>
public class OrderMovesTests
{
    private const int TenantId = 7;
    private const int Adviser = 21;
    private const int ScheduleId = 9;
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly DateOnly Settles = new(2026, 11, 25);
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);

    /// <summary>0.5 % with a minimum of 5.00, as the example of spec §8.1.</summary>
    private static readonly OrderFeeTerms Fees = Terms(5m, null);

    private static OrderFeeTerms Terms(decimal minimum, decimal? cap)
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, Usd, new DateOnly(2026, 11, 1), minimum, cap, [new FeeBand(0m, 0.5m)], new DateOnly(2026, 10, 15));
        schedule.Id = ScheduleId;
        return new OrderFeeTerms(schedule, 2);
    }

    /// <summary>10 at a reference price of 100: a buy limit of 105, a sell limit of 95.</summary>
    private static Order Placed(OrderSide side = OrderSide.Buy, OrderFeeTerms? fees = null) =>
        Order.Place(TenantId, 3, 11, side, 10m, "USD", 100m, Day, fees ?? Fees, "simulator", null, Adviser, At);

    private static Order Working(OrderSide side = OrderSide.Buy)
    {
        var order = Placed(side);
        order.RecordAttempt(At.AddMinutes(1));
        order.Accept("w-O1", At.AddMinutes(1));
        return order;
    }

    private static OrderFill Fill(Order order, decimal quantity, decimal price = 100.12m, long reportId = 1, OrderFeeTerms? fees = null) =>
        order.AddFill(reportId, quantity, price, Day, Settles, "CN-1", At.AddMinutes(2), fees ?? Fees);

    /// <summary>Posts a pending fill as the platform does: the fee of R33, then the order's totals.</summary>
    private static decimal Post(Order order, OrderFill fill, OrderFeeTerms? fees = null)
    {
        var gross = decimal.Round(fill.Quantity * fill.Price, 4, MidpointRounding.AwayFromZero);
        var fee = order.FeeOfFill(gross, fees ?? Fees);
        var net = order.Side == OrderSide.Buy ? -gross - fee : gross - fee;
        fill.MarkPosted(100 + (int)fill.ExecutionReportId, net, At.AddMinutes(3));
        order.RecordPostedFill(gross, fee, fees ?? Fees);
        return fee;
    }

    [Test]
    public void RecordAttempt_CountsAndStampsTheAttempt()
    {
        var order = Placed();
        order.IsUnsent.ShouldBeTrue();

        order.RecordAttempt(At.AddMinutes(1));
        order.RecordAttempt(At.AddMinutes(2));

        order.Attempts.ShouldBe(2);
        order.LastAttemptAt.ShouldBe(At.AddMinutes(2));
        order.Status.ShouldBe(OrderStatus.Placed);
        order.IsUnsent.ShouldBeFalse();
    }

    [Test]
    public void RecordAttempt_IsRefused_OnceTheOrderIsNotPlaced() =>
        Should.Throw<OrderStatusException>(() => Working().RecordAttempt(At));

    [Test]
    public void RecordAttempt_IsRefused_WithACancelRequest()
    {
        var order = Placed();
        order.RecordAttempt(At);
        order.RequestCancel(Adviser, null, At);

        Should.Throw<OrderStatusException>(() => order.RecordAttempt(At));
    }

    [Test]
    public void Accept_MakesAPlacedOrderWorking_WithTheBrokersId_AndClearsTheLastFailure()
    {
        var order = Placed();
        order.RecordAttempt(At);
        order.RecordFailure(BrokerFailure.Timeout);
        order.RecordAttempt(At.AddMinutes(1));

        order.Accept("w-O1", At.AddMinutes(1));

        order.Status.ShouldBe(OrderStatus.Working);
        order.BrokerOrderId.ShouldBe("w-O1");
        order.AcceptedAt.ShouldBe(At.AddMinutes(1));
        order.LastFailure.ShouldBeNull();
        order.IsOpen.ShouldBeTrue();
        order.ReservedCash.ShouldBe(1055.25m);
    }

    [Test]
    public void Accept_OnAnOrderAReportAlreadyMovedOn_KeepsItsStatus_AndOnlyTakesTheId()
    {
        var order = Placed();
        order.RecordAttempt(At);
        Fill(order, 4m);

        order.Accept("w-O1", At.AddMinutes(5));

        order.Status.ShouldBe(OrderStatus.PartlyFilled);
        order.BrokerOrderId.ShouldBe("w-O1");
        order.AcceptedAt.ShouldBeNull();
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Accept_RefusesAnEmptyBrokerOrderId(string id) =>
        Should.Throw<DomainException>(() => Placed().Accept(id, At));

    [Test]
    public void Accept_RefusesABrokerOrderIdOver100() =>
        Should.Throw<DomainException>(() => Placed().Accept(new string('x', 101), At));

    [Test]
    public void Reject_MakesAPlacedOrderRejected_WithTheReason_AndReleasesTheReservation()
    {
        var order = Placed();
        order.RecordAttempt(At);

        order.Reject(OrderRejectReason.MarketClosed, At.AddMinutes(1));

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectReason.ShouldBe(OrderRejectReason.MarketClosed);
        order.ClosedAt.ShouldBe(At.AddMinutes(1));
        order.ReservedCash.ShouldBe(0m);
        order.OpenQuantity.ShouldBe(0m);
        order.IsOpen.ShouldBeFalse();
        order.IsLive.ShouldBeFalse();
    }

    [Test]
    public void Reject_IsRefused_OnceTheOrderIsNotPlaced() =>
        Should.Throw<OrderStatusException>(() => Working().Reject(OrderRejectReason.Refused, At));

    [Test]
    public void RecordFailure_KeepsTheOrderPlaced_WithTheFailure()
    {
        var order = Placed();
        order.RecordAttempt(At);

        order.RecordFailure(BrokerFailure.Unreachable);

        order.Status.ShouldBe(OrderStatus.Placed);
        order.LastFailure.ShouldBe(BrokerFailure.Unreachable);
        order.ReservedCash.ShouldBe(1055.25m);
    }

    [Test]
    public void RecordFailure_RefusesInvalidItem_WhichIsAFailureOfTheFeedOnly() =>
        Should.Throw<DomainException>(() => Placed().RecordFailure(BrokerFailure.InvalidItem));

    [Test]
    public void CancelUnsent_CancelsAPlacedOrderWithNoAttempt_AndReleasesTheReservation()
    {
        var order = Placed();

        order.CancelUnsent(Adviser, "  Customer changed their mind ", At.AddMinutes(1));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.ClosedAt.ShouldBe(At.AddMinutes(1));
        order.CloseReason.ShouldBeNull();
        order.CancelRequestedAt.ShouldBe(At.AddMinutes(1));
        order.CancelRequestedByUserId.ShouldBe(Adviser);
        order.CancelNote.ShouldBe("Customer changed their mind");
        order.ReservedCash.ShouldBe(0m);
        order.IsLive.ShouldBeFalse();
    }

    [Test]
    public void CancelUnsent_OfASell_ReleasesItsQuantity()
    {
        var order = Placed(OrderSide.Sell);

        order.CancelUnsent(null, null, At);

        order.ReservedQuantity.ShouldBe(0m);
        order.CancelRequestedByUserId.ShouldBeNull();
    }

    [Test]
    public void CancelUnsent_IsRefused_AfterASendAttempt()
    {
        var order = Placed();
        order.RecordAttempt(At);

        Should.Throw<OrderStatusException>(() => order.CancelUnsent(Adviser, null, At));
    }

    [Test]
    public void RequestCancel_RecordsTheRequest_AndChangesNeitherTheStatusNorTheReservation()
    {
        var order = Working();

        order.RequestCancel(Adviser, " Wrong quantity ", At.AddMinutes(5));

        order.Status.ShouldBe(OrderStatus.Working);
        order.HasCancelRequest.ShouldBeTrue();
        order.CancelRequestedAt.ShouldBe(At.AddMinutes(5));
        order.CancelRequestedByUserId.ShouldBe(Adviser);
        order.CancelNote.ShouldBe("Wrong quantity");
        order.CancelAnsweredAt.ShouldBeNull();
        order.ReservedCash.ShouldBe(1055.25m);
    }

    [Test]
    public void RequestCancel_ByThePlatform_HasNoPerson()
    {
        var order = Working();

        order.RequestCancel(null, Order.CorporateActionNote, At);

        order.CancelRequestedByUserId.ShouldBeNull();
        order.CancelNote.ShouldBe("Corporate action");
    }

    [Test]
    public void RequestCancel_Twice_Throws()
    {
        var order = Working();
        order.RequestCancel(Adviser, null, At);

        Should.Throw<OrderStatusException>(() => order.RequestCancel(Adviser, null, At))
            .Message.ShouldBe("A cancellation was already requested for this order.");
    }

    [Test]
    public void RequestCancel_OfAnOrderThatIsNotOpen_Throws()
    {
        var order = Working();
        Post(order, Fill(order, 10m));

        Should.Throw<OrderStatusException>(() => order.RequestCancel(Adviser, null, At))
            .Message.ShouldBe("This order is Filled and cannot be cancelled.");
    }

    [Test]
    public void RequestCancel_RefusesANoteOver200() =>
        Should.Throw<DomainException>(() => Working().RequestCancel(Adviser, new string('n', 201), At));

    [Test]
    public void AnswerCancel_MarksTheRequestAnswered()
    {
        var order = Working();
        order.RequestCancel(Adviser, null, At);

        order.AnswerCancel(At.AddMinutes(1));

        order.CancelAnsweredAt.ShouldBe(At.AddMinutes(1));
        order.Status.ShouldBe(OrderStatus.Working);
    }

    [Test]
    public void AnswerCancel_WithoutARequest_Throws() =>
        Should.Throw<OrderStatusException>(() => Working().AnswerCancel(At));

    [Test]
    public void EndAsUnknown_CancelsAnOpenOrderWithACancelRequest_AsNotAtTheBroker()
    {
        var order = Placed();
        order.RecordAttempt(At);
        order.RequestCancel(Adviser, null, At);

        order.EndAsUnknown(At.AddMinutes(1), Fees);

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CloseReason.ShouldBe("Not at the broker");
        order.ClosedAt.ShouldBe(At.AddMinutes(1));
        order.CancelAnsweredAt.ShouldBe(At.AddMinutes(1));
        order.ReservedCash.ShouldBe(0m);
    }

    [Test]
    public void EndAsUnknown_WithoutACancelRequest_Throws() =>
        Should.Throw<OrderStatusException>(() => Working().EndAsUnknown(At, Fees));

    [Test]
    public void AddFill_AddsAPendingFill_WithWhatTheBrokerSaid()
    {
        var order = Working();

        var fill = order.AddFill(41, 4m, 100.12m, Day, Settles, "  CN-1 ", At.AddMinutes(2), Fees);

        order.Fills.ShouldHaveSingleItem().ShouldBeSameAs(fill);
        fill.PublicId.ShouldNotBe(Guid.Empty);
        fill.TenantId.ShouldBe(TenantId);
        fill.ExecutionReportId.ShouldBe(41);
        fill.Quantity.ShouldBe(4m);
        fill.Price.ShouldBe(100.12m);
        fill.TradeDate.ShouldBe(Day);
        fill.SettlementDate.ShouldBe(Settles);
        fill.BrokerReference.ShouldBe("CN-1");
        fill.Status.ShouldBe(OrderFillStatus.Pending);
        fill.HoldReason.ShouldBeNull();
        fill.TransactionId.ShouldBeNull();
        fill.IsSettled.ShouldBeFalse();
        order.FilledQuantity.ShouldBe(4m);
        order.OpenQuantity.ShouldBe(6m);
    }

    [Test]
    public void AddFill_CutsTheBrokerReferenceTo50_AndKeepsNoneForABlankOne()
    {
        var order = Working();

        order.AddFill(1, 1m, 100m, Day, Settles, new string('c', 60), At, Fees).BrokerReference.ShouldBe(new string('c', 50));
        order.AddFill(2, 1m, 100m, Day, Settles, " ", At, Fees).BrokerReference.ShouldBeNull();
    }

    [Test]
    public void AFill_OnAWorkingOrder_MakesItPartlyFilled_AndTheLastOneFilled()
    {
        var order = Working();

        Fill(order, 4m);
        order.Status.ShouldBe(OrderStatus.PartlyFilled);
        order.ClosedAt.ShouldBeNull();

        Fill(order, 6m, reportId: 2);
        order.Status.ShouldBe(OrderStatus.Filled);
        order.ClosedAt.ShouldBe(At.AddMinutes(2));
        order.OpenQuantity.ShouldBe(0m);
        order.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void AFill_BeforeTheAcceptedAnswer_MovesAPlacedOrderOn()
    {
        var order = Placed();
        order.RecordAttempt(At);

        Fill(order, 4m);

        order.Status.ShouldBe(OrderStatus.PartlyFilled);
    }

    [Test]
    public void AFill_ThatCrossedACancellation_IsAddedToTheCancelledOrder_WhichStaysCancelledAndIsLive()
    {
        var order = Working();
        order.End(OrderStatus.Cancelled, "Requested", At.AddMinutes(1), Fees);

        Fill(order, 4m);

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.ClosedAt.ShouldBe(At.AddMinutes(1));
        order.FilledQuantity.ShouldBe(4m);
        order.OpenQuantity.ShouldBe(0m);
        order.IsOpen.ShouldBeFalse();
        order.IsLive.ShouldBeTrue();
    }

    [Test]
    public void AFill_OfTheWholeQuantity_MakesAnEndedOrderFilled()
    {
        var expired = Working();
        expired.End(OrderStatus.Expired, null, At.AddMinutes(1), Fees);
        Fill(expired, 10m);
        expired.Status.ShouldBe(OrderStatus.Filled);

        var rejected = Placed();
        rejected.Reject(OrderRejectReason.Refused, At);
        Fill(rejected, 10m);
        rejected.Status.ShouldBe(OrderStatus.Filled);
        rejected.RejectReason.ShouldBeNull();
    }

    [Test]
    public void AddFill_BeyondTheQuantity_Throws()
    {
        var order = Working();
        Fill(order, 4m);

        Should.Throw<DomainException>(() => Fill(order, 6.00000001m, reportId: 2));
        order.FilledQuantity.ShouldBe(4m);
        order.Fills.Count.ShouldBe(1);
    }

    [TestCase(0, 100)]
    [TestCase(-1, 100)]
    [TestCase(1, 0)]
    [TestCase(1, -5)]
    public void AddFill_RefusesAQuantityOrAPriceNotAboveZero(decimal quantity, decimal price) =>
        Should.Throw<DomainException>(() => Fill(Working(), quantity, price));

    [Test]
    public void AddFill_RefusesASettlementDateBeforeTheTradeDate() =>
        Should.Throw<DomainException>(() => Working().AddFill(1, 1m, 100m, Day, Day.AddDays(-1), null, At, Fees));

    [TestCase(OrderStatus.Cancelled)]
    [TestCase(OrderStatus.Expired)]
    public void End_EndsAnOpenOrder_WithTheBrokersReason_AndKeepsItsFills(OrderStatus status)
    {
        var order = Working();
        Post(order, Fill(order, 4m));

        order.End(status, "  " + new string('r', 150), At.AddMinutes(9), Fees);

        order.Status.ShouldBe(status);
        order.ClosedAt.ShouldBe(At.AddMinutes(9));
        order.CloseReason.ShouldBe(new string('r', 140));
        order.Fills.Count.ShouldBe(1);
        order.FilledQuantity.ShouldBe(4m);
        order.OpenQuantity.ShouldBe(0m);
        order.ReservedCash.ShouldBe(0m);
        order.IsLive.ShouldBeFalse();
    }

    [Test]
    public void End_KeepsNoReason_ForABlankOne()
    {
        var order = Working();

        order.End(OrderStatus.Expired, " ", At, Fees);

        order.CloseReason.ShouldBeNull();
    }

    [Test]
    public void End_OfAnOrderThatIsNotOpen_Throws()
    {
        var order = Working();
        order.End(OrderStatus.Cancelled, null, At, Fees);

        Should.Throw<OrderStatusException>(() => order.End(OrderStatus.Expired, null, At, Fees));
    }

    [TestCase(OrderStatus.Filled)]
    [TestCase(OrderStatus.Rejected)]
    [TestCase(OrderStatus.Working)]
    public void End_TakesOnlyCancelledOrExpired(OrderStatus status) =>
        Should.Throw<DomainException>(() => Working().End(status, null, At, Fees));

    [Test]
    public void ABuy_ReservesTheMostThatIsLeftCanCost_ThroughItsFills()
    {
        var order = Working();
        order.ReservedCash.ShouldBe(1055.25m);

        // A pending fill is still to be posted: nothing is released yet.
        var first = Fill(order, 4m, 100.12m);
        order.ReservedCash.ShouldBe(1055.25m);

        // Posted: 400.48 gross and the minimum fee of 5.00. 6 are left: 630 at the limit, and the fee on
        // 1030.48 is 5.15, of which 5.00 is charged.
        Post(order, first).ShouldBe(5.00m);
        order.PostedGross.ShouldBe(400.48m);
        order.FeeCharged.ShouldBe(5.00m);
        order.ReservedCash.ShouldBe(630.15m);

        // The last fill is pending: the order is Filled but still live and still reserves.
        var second = Fill(order, 6m, 100.17m, reportId: 2);
        order.Status.ShouldBe(OrderStatus.Filled);
        order.IsLive.ShouldBeTrue();
        order.ReservedCash.ShouldBe(630.15m);

        // The fee on 1001.50 is 5.01, so the second fill pays 0.01.
        Post(order, second).ShouldBe(0.01m);
        order.PostedGross.ShouldBe(1001.50m);
        order.FeeCharged.ShouldBe(5.01m);
        order.ReservedCash.ShouldBe(0m);
        order.IsLive.ShouldBeFalse();
    }

    [Test]
    public void ABuy_ThatEnded_ReservesOnlyForItsPendingFills()
    {
        var order = Working();
        Post(order, Fill(order, 4m, 100.12m));
        order.End(OrderStatus.Cancelled, "Requested", At, Fees);
        order.ReservedCash.ShouldBe(0m);

        // A fill that crossed the cancellation: 2 at the limit is 210, and the fee on 610.48 is 5.00, all charged.
        var late = Fill(order, 2m, 101m, reportId: 2);
        order.ReservedCash.ShouldBe(210m);

        Post(order, late);
        order.ReservedCash.ShouldBe(0m);
        order.IsLive.ShouldBeFalse();
    }

    [Test]
    public void ABuy_WithNoSchedule_ReservesNoFee()
    {
        var order = Placed(fees: OrderFeeTerms.None);

        Post(order, Fill(order, 4m, 100m, fees: OrderFeeTerms.None), OrderFeeTerms.None).ShouldBe(0m);

        order.ReservedCash.ShouldBe(630m);
    }

    [Test]
    public void ASell_ReservesTheQuantityThatIsLeft_ThroughItsFills()
    {
        var order = Working(OrderSide.Sell);
        order.ReservedQuantity.ShouldBe(10m);

        var first = Fill(order, 4m, 99m);
        order.ReservedQuantity.ShouldBe(10m);

        Post(order, first);
        order.ReservedQuantity.ShouldBe(6m);
        order.ReservedCash.ShouldBe(0m);

        order.End(OrderStatus.Expired, null, At, Fees);
        order.ReservedQuantity.ShouldBe(0m);

        var late = Fill(order, 1m, 99m, reportId: 2);
        order.ReservedQuantity.ShouldBe(1m);
        order.IsLive.ShouldBeTrue();

        Post(order, late);
        order.ReservedQuantity.ShouldBe(0m);
        order.IsLive.ShouldBeFalse();
    }

    // 0.5 % of 1001.50 is 5.01: above the minimum of 5.00, below it per fill.
    [TestCase(5, null, 5.01)]
    // The cap of 4.00 is the cap of the order, not of each fill.
    [TestCase(1, 4, 4.00)]
    // No limit applies: every fill pays its share, and rounding is settled on the last one.
    [TestCase(0, null, 5.01)]
    public void TheFeeOfThreeFills_AddsUpToTheFeeOfOneFillOfTheSameGross(decimal minimum, decimal? cap, decimal expected)
    {
        var fees = Terms(minimum, cap);
        var parts = Placed(fees: fees);
        var total = 0m;
        total += Post(parts, Fill(parts, 3m, 100.12m, 1, fees), fees);
        total += Post(parts, Fill(parts, 1m, 100.12m, 2, fees), fees);
        total += Post(parts, Fill(parts, 6m, 100.17m, 3, fees), fees);

        var once = Placed(fees: fees);
        var whole = once.FeeOfFill(1001.50m, fees);

        parts.PostedGross.ShouldBe(1001.50m);
        total.ShouldBe(expected);
        whole.ShouldBe(expected);
        parts.FeeCharged.ShouldBe(expected);
    }

    [Test]
    public void AMove_WithFeeTermsThatAreNotTheOrders_Throws()
    {
        var order = Working();

        Should.Throw<DomainException>(() => Fill(order, 4m, fees: OrderFeeTerms.None));
        Should.Throw<DomainException>(() => order.End(OrderStatus.Cancelled, null, At, OrderFeeTerms.None));
    }

    [Test]
    public void AFill_IsPostedOnce_AndOnlyFromPending()
    {
        var order = Working();
        var fill = Fill(order, 4m);
        fill.Hold(FillHoldReason.InsufficientCash);
        fill.HoldReason.ShouldBe(FillHoldReason.InsufficientCash);
        fill.Status.ShouldBe(OrderFillStatus.Pending);

        fill.MarkPosted(77, -405.48m, At.AddMinutes(3));

        fill.Status.ShouldBe(OrderFillStatus.Posted);
        fill.TransactionId.ShouldBe(77);
        fill.NetCash.ShouldBe(-405.48m);
        fill.PostedAt.ShouldBe(At.AddMinutes(3));
        fill.HoldReason.ShouldBeNull();
        Should.Throw<DomainException>(() => fill.MarkPosted(78, -1m, At));
        Should.Throw<DomainException>(() => fill.Hold(FillHoldReason.NoFxRate));
    }

    [Test]
    public void AFill_SettlesOnce_AndCanSettleWhileItIsPending()
    {
        var fill = Fill(Working(), 4m);

        fill.Settle(Settles, At.AddDays(2));

        fill.IsSettled.ShouldBeTrue();
        fill.SettledAt.ShouldBe(At.AddDays(2));
        fill.SettledDate.ShouldBe(Settles);
        fill.Status.ShouldBe(OrderFillStatus.Pending);
        Should.Throw<DomainException>(() => fill.Settle(Settles, At.AddDays(3)));
    }
}
