using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R22, R23: the quantity and the value of a tax holding at the start and at the end of a year.</summary>
public class CustomerTaxYearValueTests
{
    [Test]
    public void QuantitiesAt_TheStartOf2026_AreThoseOfTheExample()
    {
        TaxLedger.QuantitiesAt(TaxLedger.Build(Facts), TaxYear.Start(2026)).ShouldBe([
            new TaxQuantity(A1, CustodyMode.Custodial, Aaa, 100m),
            new TaxQuantity(A2, CustodyMode.External, Aaa, 40m)
        ]);
        TaxLedger.QuantitiesAt(TaxLedger.Build(Facts), TaxYear.End(2026)).ShouldBe([new TaxQuantity(A1, CustodyMode.Custodial, Aaa, 240.8m)]);
    }

    [Test]
    public void QuantitiesAt_LeaveAReversedBuyOut_BetweenItAndItsReversal() =>
        TaxLedger.QuantitiesAt(TaxLedger.Build(Facts), Utc(2025, 6, 4, 0, 0))
            .Where(quantity => quantity.AccountId == A2).ShouldHaveSingleItem().Quantity.ShouldBe(40m);

    [Test]
    public void QuantitiesAt_CountAMoveFromItsLaterLeg()
    {
        // 12 November: T10 has left A2, T11 has not arrived on A1. The shares are still on A2.
        TaxLedger.QuantitiesAt(TaxLedger.Build(Facts), Utc(2025, 11, 12, 0, 0)).ShouldBe([
            new TaxQuantity(A1, CustodyMode.Custodial, Aaa, 140.8m),
            new TaxQuantity(A2, CustodyMode.External, Aaa, 80m)
        ]);
    }

    [Test]
    public void QuantitiesAt_CountAnEventBookedBeforeTheInstantOnly()
    {
        var at = Utc(2025, 6, 1, 0, 0);
        var buy = Fact(20, at, TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -10.00m)], [Security(20, A2, 1m, 10m)]);

        TaxLedger.QuantitiesAt(TaxLedger.Build([buy]), at).ShouldBeEmpty();
        TaxLedger.QuantitiesAt(TaxLedger.Build([buy]), at.AddTicks(1)).ShouldHaveSingleItem().Quantity.ShouldBe(1m);
    }

    [Test]
    public void WorkedExample_2025_OpensWithNothing_AndClosesWithBothAccounts()
    {
        var holding = ValuedYear(2025).Holdings.ShouldHaveSingleItem();

        var opening = holding.Opening;
        (opening.At, opening.ValuationDay, opening.Quantity, opening.Value, opening.Status).ShouldBe(
            (TaxYear.Start(2025), new DateOnly(2024, 3, 31), 0m, 0.00m, TaxValueStatus.Valued));
        opening.Lines.ShouldBeEmpty();
        var closing = holding.Closing;
        (closing.At, closing.ValuationDay, closing.Quantity, closing.Value, closing.Status).ShouldBe(
            (TaxYear.End(2025), new DateOnly(2025, 3, 31), 140m, 13_108.48m, TaxValueStatus.Valued));
        closing.Lines.Select(line => (line.AccountId, line.CustodyMode, line.Line.Quantity, line.Line.MarketValue, line.Line.Value)).ShouldBe([
            (A1, CustodyMode.Custodial, 100m, 5_320.00m, 9_363.20m),
            (A2, CustodyMode.External, 40m, 2_128.00m, 3_745.28m)
        ]);
        holding.Events.Select(e => e.QuantityAfter).ShouldBe([100m, 140m]);
    }

    [Test]
    public void WorkedExample_2026_OpensWithTheClosingOf2025_AndClosesOnA1()
    {
        var holding = ValuedYear(2026).Holdings.ShouldHaveSingleItem();

        (holding.Opening.Quantity, holding.Opening.Value, holding.Opening.Status).ShouldBe((140m, 13_108.48m, TaxValueStatus.Valued));
        (holding.Closing.Quantity, holding.Closing.Value).ShouldBe((240.8m, 13_026.08m));
        var line = holding.Closing.Lines.ShouldHaveSingleItem();
        (line.AccountId, line.Line.Price, line.Line.Fx).ShouldBe((A1, Price(2026, 3, 31, 31.00m), Rate(2026, 3, 31, 1.7450m)));
        holding.Events.Select(e => e.QuantityAfter).ShouldBe([110m, 110m, 150m, 220m, 220m, 220.8m, 220.8m, 240.8m]);
    }

    [Test]
    public void TheClosingOf2025_EqualsTheOpeningOf2026()
    {
        var closing = ValuedYear(2025).Holdings.ShouldHaveSingleItem().Closing;
        var opening = ValuedYear(2026).Holdings.ShouldHaveSingleItem().Opening;

        (opening.At, opening.ValuationDay, opening.Quantity, opening.Value, opening.Status)
            .ShouldBe((closing.At, closing.ValuationDay, closing.Quantity, closing.Value, closing.Status));
        opening.Lines.ShouldBe(closing.Lines);
    }

    [Test]
    public void TheLastQuantityAfter_EqualsTheClosingQuantity()
    {
        var holding = ValuedYear(2026).Holdings.ShouldHaveSingleItem();

        holding.Events[^1].QuantityAfter.ShouldBe(holding.Closing.Quantity);
    }

    [TestCase(FifTreatment.NotForeign)]
    [TestCase(FifTreatment.ExemptAsxListed)]
    [TestCase(FifTreatment.ExemptOther)]
    public void AnExemptOrNewZealandHolding_NeedsNoValue(FifTreatment treatment)
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Aaa] = new(treatment, 2025, null, "NZ") };

        var holding = Year(2026, treatments: treatments).Holdings.ShouldHaveSingleItem();

        (holding.Opening.Quantity, holding.Opening.Value, holding.Opening.Status).ShouldBe((140m, null, TaxValueStatus.NotRequired));
        (holding.Closing.Quantity, holding.Closing.Value, holding.Closing.Status).ShouldBe((240.8m, null, TaxValueStatus.NotRequired));
        holding.Opening.Lines.ShouldBeEmpty();
        CustomerTaxYear.NeedsValue(treatment).ShouldBeFalse();
    }

    [TestCase(FifTreatment.Attributing)]
    [TestCase(FifTreatment.AttributingNoFdr)]
    [TestCase(FifTreatment.NotClassified)]
    public void AnAttributingOrNotClassifiedHolding_NeedsAValue(FifTreatment treatment) =>
        CustomerTaxYear.NeedsValue(treatment).ShouldBeTrue();

    [Test]
    public void ALineThatIsNotValued_LeavesTheValueNull()
    {
        var events = TaxLedger.Build(Facts);
        var values = ValuesOf(2026, events, prices: [Price(2026, 3, 31, 31.00m)]);

        var holding = Year(2026, values: values).Holdings.ShouldHaveSingleItem();

        (holding.Opening.Value, holding.Opening.Status).ShouldBe((null, TaxValueStatus.NotValued));
        holding.Opening.Lines.Select(line => line.Line.Status).ShouldBe([ValuationLineStatus.NoPrice, ValuationLineStatus.NoPrice]);
        (holding.Closing.Value, holding.Closing.Status).ShouldBe((13_026.08m, TaxValueStatus.Valued));
    }

    [Test]
    public void ALineThatWasNotGiven_IsNoPrice()
    {
        var holding = Year(2026).Holdings.ShouldHaveSingleItem();

        (holding.Opening.Value, holding.Opening.Status).ShouldBe((null, TaxValueStatus.NotValued));
        holding.Opening.Lines.Select(line => (line.AccountId, line.Line.Status, line.Line.LedgerQuantity)).ShouldBe([
            (A1, ValuationLineStatus.NoPrice, 100m),
            (A2, ValuationLineStatus.NoPrice, 40m)
        ]);
    }

    [Test]
    public void AHoldingHeldAllYear_WithNoEvent_IsListed()
    {
        var holding = ValuedYear(2026, [T1, T2]).Holdings.ShouldHaveSingleItem();

        holding.Events.ShouldBeEmpty();
        (holding.Opening.Quantity, holding.Closing.Quantity).ShouldBe((140m, 140m));
    }

    [Test]
    public void AYearWithNothingHeldAndNoEvent_ListsNoHolding()
    {
        var year = ValuedYear(2024);

        year.Holdings.ShouldBeEmpty();
        (year.Complete, year.IncompleteCount).ShouldBe((true, 0));
    }

    [Test]
    public void AHoldingSoldOutBeforeTheYear_IsNotListed()
    {
        var sell = Fact(30, Utc(2025, 3, 20, 0, 0), TransactionType.Sell, [Cash(A1, CashLegRole.Principal, 5_000.00m)], [Security(30, A1, -100m, -5_005m)]);

        ValuedYear(2026, [T1, sell]).Holdings.ShouldBeEmpty();
    }
}
