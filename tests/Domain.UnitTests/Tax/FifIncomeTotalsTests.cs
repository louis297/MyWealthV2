using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.FifExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R19–R24: the totals of both methods, the quick sale adjustment, which total is lower, and when
/// no total is given.</summary>
public class FifIncomeTotalsTests
{
    private static TaxHolding SssNoFdr => Holding(500m, 5_000.00m, 4_000.00m, [], Sss, FifTreatment.AttributingNoFdr);

    [Test]
    public void Example3_TheAdjustmentIsTheLesserOfTheTwoTotals_NotTheSumOfTheLessers()
    {
        var income = Compute([Example2(), Example3Rrr()]);

        income.Rate.ShouldBe(0.05m);
        income.Fdr.ShouldBe(new FifFdrTotals(20_000.00m, 1_000.00m, 700.00m, 360.00m, 360.00m, 1_360.00m));
        income.Cv.ShouldBe(new FifCvTotals(-1_740.00m, 0.00m));
        income.Lower.ShouldBe(FifLowerMethod.Cv);
        (income.Complete, income.Incomplete.Count).ShouldBe((true, 0));
        // Per tax holding the lesser would be 550.00 + 0.00.
        income.Holdings.Select(holding => Math.Min(holding.QuickSale!.PeakHoldingAmount, Math.Max(0m, holding.QuickSale.GainAmount)))
            .Sum().ShouldBe(550.00m);
    }

    [Test]
    public void S8Example5_ATotalGainBelowZero_GivesAnAdjustmentOf0()
    {
        var income = Compute(
        [
            Holding(20_000m, 120_000m, 138_000m, [Buy(5_000m, 30_000m), Dividend(2_000m), Sell(2_000m, 14_000m)], 1),
            Holding(10_000m, 80_000m, 96_000m, [Buy(5_000m, 40_000m), Sell(3_000m, 18_000m)], 2)
        ]);

        income.Fdr.ShouldBe(new FifFdrTotals(200_000m, 10_000.00m, 1_800.00m, -3_840.00m, 0.00m, 10_000.00m));
    }

    [Test]
    public void S1Page14_TheCvIncomeIs0ForASumOfMinus8000()
    {
        var income = Compute(
        [
            Holding(1m, 100_000m, 102_000m, [], 1),
            Holding(1m, 100_000m, 110_000m, [], 2),
            Holding(1m, 100_000m, 80_000m, [], 3)
        ]);

        income.Fdr.ShouldNotBeNull().Income.ShouldBe(15_000.00m);
        income.Cv.ShouldBe(new FifCvTotals(-8_000m, 0.00m));
        income.Lower.ShouldBe(FifLowerMethod.Cv);
    }

    [Test]
    public void Lower_IsFdr_Cv_OrEqual()
    {
        // S8 Example 1: CV 25,000 against FDR 5,000.
        Compute([Holding(1_000m, 100_000m, 115_000m, [Dividend(10_000m)])]).Lower.ShouldBe(FifLowerMethod.Fdr);
        Compute([Holding(1_000m, 100_000m, 102_000m, [Dividend(1_000m)])]).Lower.ShouldBe(FifLowerMethod.Cv);
        Compute([Holding(1_000m, 1_000.00m, 1_050.00m, [])]).Lower.ShouldBe(FifLowerMethod.Equal);
    }

    [Test]
    public void ATotal_IsTheSumOfTheRoundedFiguresOfTheTaxHoldings()
    {
        // 5.01 + 5.01, where 0.05 x 200.20 would be 10.01.
        Compute([Holding(10m, 100.10m, 100.10m, [], 1), Holding(10m, 100.10m, 100.10m, [], 2)]).Fdr.ShouldNotBeNull().Amount.ShouldBe(10.02m);
    }

    [TestCase(FifTreatment.NotClassified, FifIncomeReason.NotClassified)]
    [TestCase(FifTreatment.Attributing, FifIncomeReason.HoldingIncomplete)]
    public void ANotClassifiedOrAnIncompleteHolding_GivesNoTotals(FifTreatment treatment, FifIncomeReason reason)
    {
        var missing = Holding(500m, 5_000.00m, null, [], Sss, treatment, [TaxIncompleteReason.ClosingNotValued]);

        var income = Compute([Example2(), missing]);

        (income.Fdr, income.Cv, income.Lower).ShouldBe((null, null, null));
        (income.Rate, income.Complete).ShouldBe((0.05m, false));
        income.Incomplete.ShouldBe([new FifIncomeIncomplete(reason, Sss)]);
        // The figures of the computed holding are still there.
        income.Holdings[0].Fdr!.Amount.ShouldBe(1_000.00m);
    }

    [Test]
    public void FdrNotAllowedAlone_KeepsTheTotals_WithCompleteFalse()
    {
        var income = Compute([Example2(), Example3Rrr(), SssNoFdr]);

        income.Fdr.ShouldBe(new FifFdrTotals(20_000.00m, 1_000.00m, 700.00m, 360.00m, 360.00m, 1_360.00m));
        income.Cv.ShouldBe(new FifCvTotals(-1_740.00m, 0.00m));
        income.Lower.ShouldBe(FifLowerMethod.Cv);
        income.Complete.ShouldBeFalse();
        income.Incomplete.ShouldBe([new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Sss)]);
    }

    [Test]
    public void FdrNotAllowedBesideANotClassifiedHolding_GivesNoTotals_AndListsBoth()
    {
        var notClassified = Holding(10m, 100.00m, 100.00m, [], 9, FifTreatment.NotClassified, [TaxIncompleteReason.NotClassified]);

        var income = Compute([SssNoFdr, notClassified]);

        income.Fdr.ShouldBeNull();
        income.Incomplete.ShouldBe([new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Sss), new FifIncomeIncomplete(FifIncomeReason.NotClassified, 9)]);
    }

    [Test]
    public void AnExemptAndANotForeignHolding_AreNotRequired_AndInNoTotal()
    {
        var income = Compute(
        [
            Holding(1_000m, 100_000m, 102_000m, [], 1),
            Holding(1_000m, 100_000m, 150_000m, [], 2, FifTreatment.ExemptAsxListed),
            Holding(1_000m, 100_000m, 150_000m, [], 3, FifTreatment.NotForeign)
        ]);

        income.Fdr.ShouldBe(new FifFdrTotals(100_000m, 5_000.00m, 0.00m, 0.00m, 0.00m, 5_000.00m));
        income.Cv.ShouldBe(new FifCvTotals(2_000m, 2_000m));
        (income.Complete, income.Incomplete.Count).ShouldBe((true, 0));
    }

    [Test]
    public void ACustomerWithNothing_Has0_Equal_AndIsComplete()
    {
        var income = Compute([]);

        income.ShouldBe(income with
        {
            Rate = 0.05m,
            Fdr = new FifFdrTotals(0.00m, 0.00m, 0.00m, 0.00m, 0.00m, 0.00m),
            Cv = new FifCvTotals(0.00m, 0.00m),
            Lower = FifLowerMethod.Equal,
            Complete = true
        });
        (income.Incomplete.Count, income.Holdings.Count).ShouldBe((0, 0));
        income.Fdr!.Income.ToString().ShouldBe("0.00");
    }
}
