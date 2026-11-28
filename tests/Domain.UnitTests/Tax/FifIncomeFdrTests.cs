using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.FifExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R3, R4, R7, R8: which tax holdings are computed, and the FDR amount of each.</summary>
public class FifIncomeFdrTests
{
    [Test]
    public void TheFdrAmount_IsTheRateTimesTheOpeningValue_RoundedAwayFromZero()
    {
        IncomeOf(Holding(100m, 13_108.48m, 13_026.08m, [])).Fdr.ShouldBe(new FifFdr(13_108.48m, 655.42m));
        // 0.05 x 100.10 = 5.005.
        IncomeOf(Holding(10m, 100.10m, 100.10m, [])).Fdr.ShouldBe(new FifFdr(100.10m, 5.01m));
    }

    [Test]
    public void AHoldingBoughtDuringTheYear_GivesNoFdrAmount()
    {
        IncomeOf(Holding(0m, 0.00m, 13_108.48m, [Buy(140m, 12_355.70m)])).Fdr.ShouldBe(new FifFdr(0.00m, 0.00m));
    }

    [Test]
    public void AHoldingSoldInFullDuringTheYear_GivesItsWholeAmount()
    {
        IncomeOf(Holding(1_000m, 20_000.00m, 0.00m, [Sell(1_000m, 25_000.00m)])).Fdr.ShouldBe(new FifFdr(20_000.00m, 1_000.00m));
    }

    [Test]
    public void DividendsSalesAndTheClosingValue_AddNothing()
    {
        var plain = IncomeOf(Holding(1_000m, 20_000.00m, 20_000.00m, []));
        var busy = IncomeOf(Holding(1_000m, 20_000.00m, 90_000.00m, [Dividend(700.00m, 50.00m), Sell(300m, 9_000.00m)]));

        busy.Fdr.ShouldBe(plain.Fdr);
        busy.Fdr!.Amount.ShouldBe(1_000.00m);
    }

    [Test]
    public void S1Page14_ThreeCompaniesWorth100000AtTheStart_Give5000Each()
    {
        var incomes = Compute(
        [
            Holding(1m, 100_000m, 102_000m, [], 1),
            Holding(1m, 100_000m, 110_000m, [], 2),
            Holding(1m, 100_000m, 80_000m, [], 3)
        ]).Holdings;

        incomes.Select(income => income.Fdr!.Amount).ShouldBe([5_000.00m, 5_000.00m, 5_000.00m]);
    }

    [Test]
    public void S2Example1_Jax_NoIncomeIn2022_And2750In2023()
    {
        IncomeOf(Holding(0m, 0.00m, 55_000.00m, [Buy(1_000m, 55_000.00m)], taxYear: 2022), 2022).Fdr!.Amount.ShouldBe(0.00m);
        IncomeOf(Holding(1_000m, 55_000.00m, 59_125.00m, [], taxYear: 2023), 2023).Fdr!.Amount.ShouldBe(2_750.00m);
    }

    [TestCase(2025, 0.00)]
    [TestCase(2026, 655.42)]
    public void Example1_TheWorkedExampleOfTaxYearEvents(int taxYear, decimal amount)
    {
        var year = TaxWorkedExample.ValuedYear(taxYear);

        var income = FifIncome.Compute(taxYear, year.Holdings, 2).Holdings.ShouldHaveSingleItem();

        income.Status.ShouldBe(FifIncomeStatus.Computed);
        income.Fdr!.Amount.ShouldBe(amount);
    }

    [TestCase(FifTreatment.NotForeign, FifIncomeStatus.NotRequired, null)]
    [TestCase(FifTreatment.ExemptAsxListed, FifIncomeStatus.NotRequired, null)]
    [TestCase(FifTreatment.ExemptOther, FifIncomeStatus.NotRequired, null)]
    [TestCase(FifTreatment.NotClassified, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified)]
    [TestCase(FifTreatment.AttributingNoFdr, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed)]
    public void EachTreatment_HasItsStatusAndReason_AndNoFigure(FifTreatment treatment, FifIncomeStatus status, FifIncomeReason? reason)
    {
        var income = IncomeOf(Holding(1_000m, 20_000.00m, 20_000.00m, [Buy(10m, 200.00m)], Sss, treatment));

        income.ShouldBe(new TaxHoldingIncome(Sss, status, reason, null, null, null));
    }

    [Test]
    public void AnAttributingHoldingThatIsIncomplete_IsNotComputed_EvenWhereAFigureCouldBeWorkedOut()
    {
        var holding = Holding(1_000m, 20_000.00m, null, [], Sss, incomplete: [TaxIncompleteReason.ClosingNotValued]);

        IncomeOf(holding).ShouldBe(new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.HoldingIncomplete, null, null, null));
    }

    [Test]
    public void ACompleteAttributingHolding_IsComputed()
    {
        var income = IncomeOf(Holding(1_000m, 20_000.00m, 20_000.00m, [], Qqq));

        (income.InstrumentId, income.Status, income.Reason).ShouldBe((Qqq, FifIncomeStatus.Computed, (FifIncomeReason?)null));
    }

    [Test]
    public void TheIncomesAreInTheOrderOfTheHoldings()
    {
        Compute([Holding(1m, 1m, 1m, [], 7), Holding(1m, 1m, 1m, [], 3)]).Holdings.Select(income => income.InstrumentId).ShouldBe([7, 3]);
    }
}
