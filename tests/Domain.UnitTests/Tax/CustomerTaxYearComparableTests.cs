using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R24, R25: each quantity of the year on the basis of the year's end, and the peak.</summary>
public class CustomerTaxYearComparableTests
{
    [Test]
    public void WorkedExample_2026_RestatesEachAccountByItsOwnSplit()
    {
        var holding = ValuedYear(2026).Holdings.ShouldHaveSingleItem();

        holding.Opening.ComparableQuantity.ShouldBe(280m);
        holding.Events.Select(e => e.ComparableQuantityAfter).ShouldBe([220m, 220m, 220m, 220m, 220m, 220.8m, 220.8m, 240.8m]);
        holding.Closing.ComparableQuantity.ShouldBe(240.8m);
        holding.PeakQuantity.ShouldBe(280m);
    }

    [Test]
    public void WorkedExample_2025_HasNoReorganisation_SoEachComparableQuantityIsTheQuantity()
    {
        var holding = ValuedYear(2025).Holdings.ShouldHaveSingleItem();

        holding.Opening.ComparableQuantity.ShouldBe(0m);
        holding.Events.Select(e => e.ComparableQuantityAfter).ShouldBe([100m, 140m]);
        holding.Closing.ComparableQuantity.ShouldBe(140m);
        holding.PeakQuantity.ShouldBe(140m);
    }

    [Test]
    public void ASplitBookedOnTwoAccountsOnTwoDays_WithABuyBetween_StaysComparable()
    {
        var facts = new[]
        {
            Opening(A1, 100m),
            Opening(A2, 100m, id: 2),
            Fact(10, Utc(2025, 7, 1, 0, 0), TransactionType.Split, [], [Security(10, A2, 100m, 0m)]),
            Fact(11, Utc(2025, 7, 1, 6, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -10.00m)], [Security(11, A1, 10m, 10m)]),
            Fact(12, Utc(2025, 7, 2, 12, 0), TransactionType.Split, [], [Security(12, A1, 110m, 0m)])
        };

        var holding = ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem();

        holding.Opening.ComparableQuantity.ShouldBe(400m);
        holding.Events.Select(e => (e.QuantityAfter, e.ComparableQuantityAfter)).ShouldBe([(300m, 400m), (310m, 420m), (420m, 420m)]);
        holding.PeakQuantity.ShouldBe(420m);
    }

    [Test]
    public void ThePeak_IsReadAfterEveryEvent_SoABuyAndASaleOnOneDayShow()
    {
        var facts = new[]
        {
            Opening(A2, 100m),
            Fact(10, Utc(2025, 8, 1, 1, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -50.00m)], [Security(10, A2, 50m, 50m)]),
            Fact(11, Utc(2025, 8, 1, 4, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 50.00m)], [Security(11, A2, -50m, -50m)])
        };

        ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem().PeakQuantity.ShouldBe(150m);
    }

    [Test]
    public void AComparableQuantity_IsMultipliedFirstAndDividedAfter()
    {
        // 3 → 10: 3 × 10 / 3 = 10, where 3 × (10 / 3 to 8 places) would give 9.99999999.
        var facts = new[] { Opening(A2, 3m), Fact(10, Utc(2025, 7, 1, 0, 0), TransactionType.Split, [], [Security(10, A2, 7m, 0m)]) };

        ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem().Opening.ComparableQuantity.ShouldBe(10m);
    }

    [Test]
    public void AComparableQuantity_IsCutToEightDecimalPlaces()
    {
        // Open 1, buy 2, then consolidate 3 into 2: the opening of 1 is 1 × 2 / 3 = 0.666666666… → 0.66666666.
        var facts = new[]
        {
            Opening(A2, 1m),
            Fact(9, Utc(2025, 6, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -2.00m)], [Security(9, A2, 2m, 2m)]),
            Fact(10, Utc(2025, 7, 1, 0, 0), TransactionType.Split, [], [Security(10, A2, -1m, 0m)])
        };

        var holding = ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem();

        holding.Opening.ComparableQuantity.ShouldBe(0.66666666m);
        holding.Events.Select(e => e.ComparableQuantityAfter).ShouldBe([2m, 2m]);
        holding.PeakQuantity.ShouldBe(2m);
    }

    [Test]
    public void AReorganisationOnAnAccountThatHoldsNothingBeforeIt_LeavesNoComparableQuantity()
    {
        var facts = new[] { Opening(A1, 100m), Fact(10, Utc(2025, 7, 1, 0, 0), TransactionType.Bonus, [], [Security(10, A2, 5m, 0m)]) };

        var holding = ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem();

        holding.Opening.ComparableQuantity.ShouldBeNull();
        holding.Events.ShouldHaveSingleItem().ComparableQuantityAfter.ShouldBeNull();
        holding.Closing.ComparableQuantity.ShouldBeNull();
        holding.PeakQuantity.ShouldBeNull();
    }

    private static LedgerFact Opening(int accountId, decimal quantity, int id = 1) =>
        Fact(id, Utc(2020, 1, 1, 0, 0), TransactionType.OpeningHoldings, [], [Security(id, accountId, quantity, 0m)]);
}
