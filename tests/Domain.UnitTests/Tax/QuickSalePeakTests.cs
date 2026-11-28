using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.FifExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R9–R12: the peak holding differential, the average cost of the year's purchases and the peak
/// holding method amount of one tax holding.</summary>
public class QuickSalePeakTests
{
    private static (decimal Opening, decimal Peak, decimal Closing, decimal Differential, decimal Purchased, decimal Cost, decimal? Average,
        decimal Amount) Peak(TaxHolding holding)
    {
        var sale = IncomeOf(holding).QuickSale.ShouldNotBeNull();
        return (sale.OpeningQuantity, sale.PeakQuantity, sale.ClosingQuantity, sale.PeakHoldingDifferential, sale.PurchasedQuantity,
            sale.PurchaseCost, sale.AverageCost, sale.PeakHoldingAmount);
    }

    [Test]
    public void S1Page15_Differential2000_AverageCost22_Amount2200()
    {
        var holding = Holding(10_000m, 200_000m, 286_000m,
            [Buy(5_000m, 110_000m), Sell(4_000m, 100_000m), Buy(2_000m, 44_000m)]);

        Peak(holding).ShouldBe((10_000m, 15_000m, 13_000m, 2_000m, 7_000m, 154_000m, 22m, 2_200.00m));
    }

    [Test]
    public void S8Example4_CoA_Amount720()
    {
        var holding = Holding(10_000m, 30_000m, 48_000m,
            [Buy(1_000m, 4_000m), Buy(4_000m, 20_000m), Dividend(1_000m), Sell(3_000m, 15_000m)]);

        Peak(holding).ShouldBe((10_000m, 15_000m, 12_000m, 3_000m, 5_000m, 24_000m, 4.8m, 720.00m));
    }

    [Test]
    public void S8Example5_Amounts600And1200()
    {
        Peak(Holding(20_000m, 120_000m, 138_000m, [Buy(5_000m, 30_000m), Dividend(2_000m), Sell(2_000m, 14_000m)])).Amount.ShouldBe(600.00m);
        Peak(Holding(10_000m, 80_000m, 96_000m, [Buy(5_000m, 40_000m), Sell(3_000m, 18_000m)])).Amount.ShouldBe(1_200.00m);
    }

    [Test]
    public void Example2_TheSharesBoughtBeforeTheSplit_CountOnTheBasisOfTheYearsEnd()
    {
        Peak(Example2()).ShouldBe((2_000m, 3_200m, 2_200m, 1_000m, 1_700m, 18_700.00m, 11m, 550.00m));
    }

    [Test]
    public void AHoldingOnlyIncreased_OrOnlyDecreased_HasNoDifferential()
    {
        Peak(Holding(1_000m, 10_000m, 13_000m, [Buy(100m, 1_000m), Buy(200m, 2_000m)])).Differential.ShouldBe(0m);
        Peak(Holding(1_000m, 10_000m, 13_000m, [Buy(100m, 1_000m), Buy(200m, 2_000m)])).Amount.ShouldBe(0.00m);
        Peak(Holding(1_000m, 10_000m, 5_000m, [Sell(300m, 3_000m), Sell(200m, 2_000m)])).Differential.ShouldBe(0m);
    }

    [Test]
    public void ASaleThenAPurchaseThatDoesNotPassTheOpening_HasNoDifferential()
    {
        var peak = Peak(Holding(1_000m, 10_000m, 9_000m, [Sell(300m, 3_000m), Buy(200m, 2_000m)]));

        (peak.Peak, peak.Differential, peak.Average, peak.Amount).ShouldBe((1_000m, 0m, (decimal?)10m, 0.00m));
    }

    [Test]
    public void APurchaseAndASaleOnOneDay_Show()
    {
        Peak(Holding(0m, 0.00m, 0.00m, [Buy(100m, 1_000m), Sell(100m, 1_100m)]))
            .ShouldBe((0m, 100m, 0m, 100m, 100m, 1_000m, 10m, 50.00m));
    }

    [Test]
    public void AnArrival_IsAPurchaseAtItsMarketValue()
    {
        Peak(Holding(0m, 0.00m, 2_500.00m, [Arrive(100m, 5_000.00m), Sell(50m, 2_600.00m)]))
            .ShouldBe((0m, 100m, 50m, 50m, 100m, 5_000.00m, 50m, 125.00m));
    }

    [Test]
    public void NoPurchaseInTheYear_GivesNoAverageCost_And0()
    {
        Peak(Holding(1_000m, 10_000m, 0m, [Sell(1_000m, 12_000m)])).ShouldBe((1_000m, 1_000m, 0m, 0m, 0m, 0.00m, null, 0.00m));
    }

    [Test]
    public void TheAverageCost_IsGivenTo8Places_AndTheAmountIsMultipliedFirst()
    {
        Peak(Holding(0m, 0.00m, 0.00m, [Buy(3m, 100.00m), Sell(1m, 40.00m)])).Average.ShouldBe(33.33333333m);

        // 0.05 x 300,000,000 x 10,000,000,000.00 / 300,000,000 = 500,000,000.00; with the average cut to 8 places first it
        // would be 0.05 x 300,000,000 x 33.33333333 = 499,999,999.95.
        Peak(Holding(0m, 0.00m, 0.00m, [Buy(300_000_000m, 10_000_000_000.00m), Sell(300_000_000m, 10_000_000_000.00m)]))
            .Amount.ShouldBe(500_000_000.00m);
    }

    [Test]
    public void AMoveAReorganisationAndACorrection_AreNotPurchases()
    {
        var peak = Peak(Holding(1_000m, 10_000m, 10_000m, [Move(), Split(1_000m), Correct(50m), Buy(10m, 100.00m)]));

        (peak.Purchased, peak.Cost).ShouldBe((10m, 100.00m));
    }
}
