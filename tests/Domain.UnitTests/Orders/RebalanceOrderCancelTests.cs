using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Rebalances R15, R19, R20 (docs/features/rebalances.md §5): a rebalance order names its rebalance, and a
/// person cancels it only by cancelling the rebalance.</summary>
public class RebalanceOrderCancelTests
{
    private const int Adviser = 21;
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);

    private static Order Placed(int? rebalanceId) =>
        Order.Place(7, 3, 11, OrderSide.Buy, 10m, "USD", 100m, Day, OrderFeeTerms.None, "simulator", "Rebalance", Adviser, At,
            rebalanceId: rebalanceId);

    private static Order Working(int? rebalanceId)
    {
        var order = Placed(rebalanceId);
        order.RecordAttempt(At.AddMinutes(1));
        order.Accept("w-O1", At.AddMinutes(1));
        return order;
    }

    [Test]
    public void Place_WithARebalance_NamesIt() => Placed(42).RebalanceId.ShouldBe(42);

    [Test]
    public void Place_WithoutOne_HasNone() => Placed(null).RebalanceId.ShouldBeNull();

    [Test]
    public void APersonsCancel_OfARebalanceOrder_IsRefused()
    {
        Should.Throw<OrderStatusException>(() => Placed(42).CancelUnsent(Adviser, null, At))
            .Message.ShouldBe("This order belongs to a rebalance. Cancel the rebalance.");
        Should.Throw<OrderStatusException>(() => Working(42).RequestCancel(Adviser, null, At))
            .Message.ShouldBe("This order belongs to a rebalance. Cancel the rebalance.");
    }

    [Test]
    public void TheCancelOfItsRebalance_AndThePlatformsRequest_AreNot()
    {
        var unsent = Placed(42);
        unsent.CancelUnsent(Adviser, Order.RebalanceCancelledNote, At, forRebalance: true);
        unsent.Status.ShouldBe(OrderStatus.Cancelled);
        unsent.CancelNote.ShouldBe("Rebalance cancelled");

        var working = Working(42);
        working.RequestCancel(null, Order.CorporateActionNote, At);
        working.HasCancelRequest.ShouldBeTrue();
    }

    [Test]
    public void APersonsCancel_OfAPersonsOrder_IsUnchanged()
    {
        var order = Placed(null);
        order.CancelUnsent(Adviser, null, At);
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }
}
