using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income §5, R2, R23: <see cref="CustomerTaxYear.Compute"/> carries the income on each tax holding and the
/// totals, and changes nothing else.</summary>
public class CustomerTaxYearIncomeTests
{
    [Test]
    public void Example1_2026_TheIncomeOfTheHoldingAndTheTotals()
    {
        var year = ValuedYear(2026);

        var income = year.FifIncome.ShouldNotBeNull();
        income.Fdr.ShouldBe(new FifFdrTotals(13_108.48m, 655.42m, 0.00m, 0.00m, 0.00m, 655.42m));
        income.Cv.ShouldBe(new FifCvTotals(1_740.87m, 1_740.87m));
        (income.Lower, income.Complete).ShouldBe(((FifLowerMethod?)FifLowerMethod.Fdr, true));
        var holding = year.Holdings.ShouldHaveSingleItem();
        holding.Income.ShouldNotBeNull().ShouldBe(income.Holdings.ShouldHaveSingleItem());
        holding.Income!.Status.ShouldBe(FifIncomeStatus.Computed);
    }

    [Test]
    public void Example1_2025_TheFdrIncomeIs0_AndTheCvIncome752_78()
    {
        var income = ValuedYear(2025).FifIncome.ShouldNotBeNull();

        (income.Fdr.ShouldNotBeNull().Income, income.Cv.ShouldNotBeNull().Income, income.Lower).ShouldBe((0.00m, 752.78m, (FifLowerMethod?)FifLowerMethod.Fdr));
    }

    [Test]
    public void AnIncompleteIncome_LeavesCompleteOfTheHoldingTheYearAndTheThresholdTestAsTheyAre()
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Aaa] = new(FifTreatment.AttributingNoFdr, 2025, null, "US") };
        var attributing = ValuedYear(2026);

        var year = ValuedYear(2026, treatments: treatments);

        var income = year.FifIncome.ShouldNotBeNull();
        (income.Complete, income.Incomplete.ShouldHaveSingleItem()).ShouldBe((false, new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Aaa)));
        (year.Complete, year.Holdings.ShouldHaveSingleItem().Complete, year.Threshold.Complete).ShouldBe((true, true, true));
        year.Threshold.ShouldBe(attributing.Threshold);
    }

    [Test]
    public void AHoldingNotValued_IsHoldingIncomplete_AndTheTotalsAreNotGiven()
    {
        var year = Year(2026);

        var income = year.FifIncome.ShouldNotBeNull();
        income.Incomplete.ShouldBe([new FifIncomeIncomplete(FifIncomeReason.HoldingIncomplete, Aaa)]);
        (income.Fdr, income.Cv).ShouldBe((null, null));
        year.Holdings.ShouldHaveSingleItem().Income.ShouldNotBeNull().Status.ShouldBe(FifIncomeStatus.NotComputed);
    }

    [Test]
    public void TheIncomeIsComputed_WhileTheThresholdTestIsNotAbove()
    {
        var year = ValuedYear(2026);

        year.Threshold.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
        year.FifIncome.ShouldNotBeNull().Fdr.ShouldNotBeNull().Income.ShouldBe(655.42m);
    }

    [Test]
    public void AnExemptHolding_IsNotRequired()
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Aaa] = new(FifTreatment.ExemptAsxListed, 2025, null, "AU") };

        var year = Year(2026, treatments: treatments);

        year.Holdings.ShouldHaveSingleItem().Income.ShouldNotBeNull().Status.ShouldBe(FifIncomeStatus.NotRequired);
        year.FifIncome.ShouldNotBeNull().Complete.ShouldBeTrue();
    }
}
