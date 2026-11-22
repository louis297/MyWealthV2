using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Orders R10, R12 (docs/features/orders.md §5): the price limit, the reference price age and the maximum gross.</summary>
public class OrderRulesTests
{
    [Test]
    public void TheCollarIsFivePercent_AndAReferencePriceMayBeFourDaysOld()
    {
        OrderRules.CollarPercent.ShouldBe(5m);
        OrderRules.ReferencePriceMaxAgeDays.ShouldBe(4);
    }

    [TestCase(100, 105)]
    [TestCase(0.12345679, 0.12962963)]
    // A midpoint rounds away from zero, as money does everywhere else.
    [TestCase(0.0000001, 0.00000011)]
    public void PriceLimit_OfABuy_IsTheReferencePriceTimes105Percent_RoundedTo8(decimal reference, decimal limit) =>
        OrderRules.PriceLimit(OrderSide.Buy, reference).ShouldBe(limit);

    [TestCase(100, 95)]
    [TestCase(0.12345679, 0.11728395)]
    [TestCase(0.0000001, 0.0000001)]
    public void PriceLimit_OfASell_IsTheReferencePriceTimes95Percent_RoundedTo8(decimal reference, decimal limit) =>
        OrderRules.PriceLimit(OrderSide.Sell, reference).ShouldBe(limit);

    [TestCase(10, 105, 1050)]
    [TestCase(3.33333333, 105.5, 351.6667)]
    [TestCase(0.00000001, 105, 0)]
    public void MaximumGross_IsTheQuantityTimesThePriceLimit_RoundedTo4(decimal quantity, decimal limit, decimal gross) =>
        OrderRules.MaximumGross(quantity, limit).ShouldBe(gross);

    [TestCase("2026-11-23", true)]
    [TestCase("2026-11-19", true)]
    [TestCase("2026-11-18", false)]
    [TestCase("2026-11-10", false)]
    public void IsReferencePriceFresh_UpToFourCalendarDaysBeforeTheDayTheOrderIsPlaced(string priceDate, bool fresh) =>
        OrderRules.IsReferencePriceFresh(DateOnly.Parse(priceDate), new DateOnly(2026, 11, 23)).ShouldBe(fresh);

    [Test]
    public void FeeTerms_WithNoSchedule_ChargeNothing() =>
        OrderFeeTerms.None.FeeFor(1050m).ShouldBe(0m);

    [TestCase(1050, 5.25)]
    [TestCase(400.48, 5.00)]
    [TestCase(1001.50, 5.01)]
    public void FeeTerms_WithASchedule_ChargeTheSchedulesFee(decimal gross, decimal fee)
    {
        var schedule = TradeFeeSchedule.Create(
            1, Currency.Create("USD", "US Dollar", 2), new DateOnly(2026, 11, 1), 5m, null, [new FeeBand(0m, 0.5m)],
            new DateOnly(2026, 10, 15));

        new OrderFeeTerms(schedule, 2).FeeFor(gross).ShouldBe(fee);
    }
}
