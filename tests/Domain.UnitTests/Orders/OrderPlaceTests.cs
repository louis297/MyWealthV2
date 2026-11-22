using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Orders R6, R10–R14 (docs/features/orders.md §5): placing an order and what it reserves.</summary>
public class OrderPlaceTests
{
    private const int TenantId = 7;
    private const int AccountId = 3;
    private const int InstrumentId = 11;
    private const int ScheduleId = 9;
    private const int Adviser = 21;
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly PriceDate = new(2026, 11, 23);
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);

    /// <summary>0.5 % with a minimum of 5.00, as the example of spec §8.1.</summary>
    private static OrderFeeTerms Fees(Currency? currency = null)
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, currency ?? Usd, new DateOnly(2026, 11, 1), 5m, null, [new FeeBand(0m, 0.5m)], new DateOnly(2026, 10, 15));
        schedule.Id = ScheduleId;
        return new OrderFeeTerms(schedule, 2);
    }

    private static Order Place(
        OrderSide side = OrderSide.Buy,
        decimal quantity = 10m,
        decimal referencePrice = 100m,
        OrderFeeTerms? fees = null,
        string currency = "USD",
        string connection = "simulator",
        string? memo = null,
        Guid? publicId = null) =>
        Order.Place(
            TenantId, AccountId, InstrumentId, side, quantity, currency, referencePrice, PriceDate, fees ?? OrderFeeTerms.None,
            connection, memo, Adviser, At, publicId);

    [Test]
    public void Place_IsPlaced_Open_Live_AndKeepsWhatItWasGiven()
    {
        var publicId = Guid.NewGuid();

        var order = Place(memo: "  Monthly top-up ", currency: " usd ", fees: Fees(), publicId: publicId);

        order.PublicId.ShouldBe(publicId);
        order.TenantId.ShouldBe(TenantId);
        order.AccountId.ShouldBe(AccountId);
        order.InstrumentId.ShouldBe(InstrumentId);
        order.Side.ShouldBe(OrderSide.Buy);
        order.Type.ShouldBe(OrderType.Market);
        order.TimeInForce.ShouldBe(TimeInForce.Day);
        order.Quantity.ShouldBe(10m);
        order.Currency.ShouldBe("USD");
        order.ReferencePrice.ShouldBe(100m);
        order.ReferencePriceDate.ShouldBe(PriceDate);
        order.TradeFeeScheduleId.ShouldBe(ScheduleId);
        order.Connection.ShouldBe("simulator");
        order.Memo.ShouldBe("Monthly top-up");
        order.PlacedByUserId.ShouldBe(Adviser);
        order.PlacedAt.ShouldBe(At);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.FilledQuantity.ShouldBe(0m);
        order.OpenQuantity.ShouldBe(10m);
        order.PostedGross.ShouldBe(0m);
        order.FeeCharged.ShouldBe(0m);
        order.IsOpen.ShouldBeTrue();
        order.IsLive.ShouldBeTrue();
        order.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Place_WithoutAPublicId_MakesOne() =>
        Place().PublicId.ShouldNotBe(Guid.Empty);

    [Test]
    public void ABuy_HasALimitFivePercentAbove_AndReservesTheMostItCanCostWithFees()
    {
        var order = Place(fees: Fees());

        order.PriceLimit.ShouldBe(105m);
        order.ReservedCash.ShouldBe(1055.25m);
        order.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void ABuy_WithNoSchedule_ReservesTheMaximumGross()
    {
        var order = Place();

        order.TradeFeeScheduleId.ShouldBeNull();
        order.ReservedCash.ShouldBe(1050m);
    }

    [Test]
    public void ABuy_BelowTheSchedulesMinimum_ReservesTheMinimumFee() =>
        Place(quantity: 1m, fees: Fees()).ReservedCash.ShouldBe(110m);

    [Test]
    public void ABuy_OfAFractionOfAShare_RoundsTheMaximumGrossTo4() =>
        Place(quantity: 0.33333333m, referencePrice: 99.99m).ReservedCash.ShouldBe(34.9965m);

    [Test]
    public void ASell_HasALimitFivePercentBelow_AndReservesItsQuantity_AndNoCash()
    {
        var order = Place(OrderSide.Sell, fees: Fees());

        order.PriceLimit.ShouldBe(95m);
        order.ReservedQuantity.ShouldBe(10m);
        order.ReservedCash.ShouldBe(0m);
        order.TradeFeeScheduleId.ShouldBe(ScheduleId);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(0.123456789)]
    public void Place_RefusesAQuantityNotAboveZero_OrWithMoreThan8Decimals(decimal quantity) =>
        Should.Throw<DomainException>(() => Place(quantity: quantity));

    [TestCase(0)]
    [TestCase(-100)]
    public void Place_RefusesAReferencePriceNotAboveZero(decimal referencePrice) =>
        Should.Throw<DomainException>(() => Place(referencePrice: referencePrice));

    [TestCase("")]
    [TestCase("US")]
    [TestCase("USDX")]
    public void Place_RefusesACurrencyThatIsNotThreeLetters(string currency) =>
        Should.Throw<DomainException>(() => Place(currency: currency));

    [TestCase("")]
    [TestCase("Simulator")]
    [TestCase("1broker")]
    public void Place_RefusesABadConnectionName(string connection) =>
        Should.Throw<DomainException>(() => Place(connection: connection));

    [Test]
    public void Place_RefusesAMemoOver200() =>
        Should.Throw<DomainException>(() => Place(memo: new string('m', 201)));

    [Test]
    public void Place_KeepsNoMemo_ForABlankOne() =>
        Place(memo: "   ").Memo.ShouldBeNull();

    [Test]
    public void Place_RefusesAScheduleForAnotherQuoteCurrency() =>
        Should.Throw<DomainException>(() => Place(fees: Fees(Currency.Create("NZD", "New Zealand Dollar", 2))));
}
