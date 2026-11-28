using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.FifExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R13–R15: the matching of the sales to the purchases of the year, last bought first sold, and
/// the quick sale gain amount of one tax holding.</summary>
public class QuickSaleGainTests
{
    private static QuickSale SaleOf(TaxHolding holding) => IncomeOf(holding).QuickSale.ShouldNotBeNull();

    private static (decimal Matched, decimal Proceeds, decimal Dividends, decimal Cost, decimal Gain) Gain(TaxHolding holding)
    {
        var sale = SaleOf(holding);
        return (sale.MatchedQuantity, sale.Proceeds, sale.Dividends, sale.Cost, sale.GainAmount);
    }

    [Test]
    public void S1Page15_TheGainIsOnThe4000SharesSold_NotOnTheDifferential()
    {
        var sale = SaleOf(Holding(10_000m, 200_000m, 286_000m, [Buy(5_000m, 110_000m), Sell(4_000m, 100_000m), Buy(2_000m, 44_000m)]));

        (sale.MatchedQuantity, sale.Proceeds, sale.Cost, sale.GainAmount).ShouldBe((4_000m, 100_000.00m, 88_000.00m, 12_000.00m));
        sale.Matches.ShouldBe([new QuickSaleMatch(1, 2, 4_000m)]);
    }

    [Test]
    public void S8Example4_CoA_Gain800_WithADividendShareOf200()
    {
        var sale = SaleOf(Holding(10_000m, 30_000m, 48_000m,
            [Buy(1_000m, 4_000m), Buy(4_000m, 20_000m), Dividend(1_000m), Sell(3_000m, 15_000m)]));

        (sale.MatchedQuantity, sale.Proceeds, sale.Dividends, sale.Cost, sale.GainAmount).ShouldBe((3_000m, 15_000.00m, 200.00m, 14_400.00m, 800.00m));
        sale.Matches.ShouldBe([new QuickSaleMatch(2, 4, 3_000m)]);
        sale.DividendShares.ShouldBe([new QuickSaleDividendShare(3, 1_000m, 15_000m, 3_000m, 200.00m)]);
    }

    [Test]
    public void S8Example5_Gains2160AndMinus6000()
    {
        Gain(Holding(20_000m, 120_000m, 138_000m, [Buy(5_000m, 30_000m), Dividend(2_000m), Sell(2_000m, 14_000m)]))
            .ShouldBe((2_000m, 14_000.00m, 160.00m, 12_000.00m, 2_160.00m));
        Gain(Holding(10_000m, 80_000m, 96_000m, [Buy(5_000m, 40_000m), Sell(3_000m, 18_000m)]))
            .ShouldBe((3_000m, 18_000.00m, 0.00m, 24_000.00m, -6_000.00m));
    }

    [Test]
    public void Example2_ToTheCent_WithItsThreeMatches()
    {
        var sale = SaleOf(Example2());

        (sale.MatchedQuantity, sale.Proceeds, sale.Dividends, sale.Cost, sale.GainAmount).ShouldBe((1_200m, 15_400.00m, 260.00m, 13_200.00m, 2_460.00m));
        sale.Matches.ShouldBe([new QuickSaleMatch(3, 5, 400m), new QuickSaleMatch(1, 5, 600m), new QuickSaleMatch(1, 7, 200m)]);
        sale.Sales.ShouldBe([new QuickSaleSale(5, 1_000m, 1_000m, 13_000.00m, 13_000.00m), new QuickSaleSale(7, 500m, 200m, 6_000.00m, 2_400.00m)]);
        sale.DividendShares.ShouldBe(
        [
            new QuickSaleDividendShare(2, 700.00m, 2_800m, 800m, 200.00m),
            new QuickSaleDividendShare(6, 660.00m, 2_200m, 200m, 60.00m)
        ]);
    }

    [Test]
    public void ASaleTakesTheMostRecentPurchaseFirst_SoTheVariantOfExample2CountsNoDividend()
    {
        // First bought, first sold would take 300 of the April purchase and count 700.00 x 300 / 2,800 = 75.00.
        var sale = SaleOf(Holding(2_000m, 20_000.00m, 23_100.00m,
        [
            new Step(TaxEventKind.Acquisition, 800m, 8_800.00m, Booked: 400m),
            Dividend(700.00m),
            new Step(TaxEventKind.Acquisition, 400m, 5_000.00m, Booked: 200m),
            Split(1_600m),
            Sell(300m, 3_900.00m)
        ], openingBooked: 1_000m));

        sale.Matches.ShouldBe([new QuickSaleMatch(3, 5, 300m)]);
        sale.DividendShares.ShouldBeEmpty();
        sale.Dividends.ShouldBe(0.00m);
    }

    [Test]
    public void ASaleOfMoreThanWasBoughtInTheYear_IsMatchedInPart_WithItsProceedsProRata()
    {
        var sale = SaleOf(Holding(1_000m, 10_000m, 8_000m, [Buy(100m, 1_000m), Sell(300m, 3_600m)]));

        sale.Sales.ShouldBe([new QuickSaleSale(2, 300m, 100m, 3_600m, 1_200.00m)]);
        (sale.Cost, sale.GainAmount).ShouldBe((1_000.00m, 200.00m));
    }

    [Test]
    public void ASaleBeforeTheFirstPurchase_MatchesNothing()
    {
        var sale = SaleOf(Holding(1_000m, 10_000m, 10_000m, [Sell(100m, 1_000m), Buy(100m, 1_100m)]));

        sale.Matches.ShouldBeEmpty();
        sale.Sales.ShouldBe([new QuickSaleSale(1, 100m, 0m, 1_000m, 0.00m)]);
        (sale.MatchedQuantity, sale.Proceeds, sale.Cost, sale.GainAmount).ShouldBe((0m, 0.00m, 0.00m, 0.00m));
    }

    [Test]
    public void ADividendBeforeThePurchaseOrAfterTheSaleOfAMatch_GivesNoShare()
    {
        var sale = SaleOf(Holding(1_000m, 10_000m, 10_000m, [Dividend(100m), Buy(100m, 1_000m), Sell(100m, 1_100m), Dividend(90m)]));

        sale.DividendShares.ShouldBeEmpty();
        sale.GainAmount.ShouldBe(100.00m);
    }

    [Test]
    public void ADividendBookedWhileNothingIsHeld_GivesNone()
    {
        var sale = SaleOf(Holding(0m, 0.00m, 0.00m, [Dividend(50m), Buy(100m, 1_000m), Sell(100m, 1_100m)]));

        sale.DividendShares.ShouldBeEmpty();
        sale.GainAmount.ShouldBe(100.00m);
    }

    [Test]
    public void TheDividendShare_IsOfTheGrossPlusTheImputationCredit_Rounded()
    {
        // (90.00 + 10.00) x 100 / 300 = 33.333... -> 33.33.
        var sale = SaleOf(Holding(200m, 2_000m, 2_000m, [Buy(100m, 1_000m), Dividend(90.00m, 10.00m), Sell(100m, 1_000m)]));

        sale.DividendShares.ShouldBe([new QuickSaleDividendShare(2, 100.00m, 300m, 100m, 33.33m)]);
    }

    [Test]
    public void ADeparture_IsASaleAtItsMarketValue()
    {
        var sale = SaleOf(Holding(0m, 0.00m, 0.00m, [Buy(100m, 1_000m), Depart(100m, 1_200m)]));

        sale.Matches.ShouldBe([new QuickSaleMatch(1, 2, 100m)]);
        sale.GainAmount.ShouldBe(200.00m);
    }

    [Test]
    public void AMoveAReorganisationAndACorrection_AreNeitherAPurchaseNorASale()
    {
        var sale = SaleOf(Holding(0m, 0.00m, 0.00m, [Buy(100m, 1_000m), Correct(50m), Move(), Split(0m), Sell(150m, 1_800m)]));

        sale.Matches.ShouldBe([new QuickSaleMatch(1, 5, 100m)]);
        sale.Sales.ShouldBe([new QuickSaleSale(5, 150m, 100m, 1_800m, 1_200.00m)]);
        sale.GainAmount.ShouldBe(200.00m);
    }

    [Test]
    public void APurchaseOnOneAccountAndASaleOnAnother_AreMatched()
    {
        var sale = SaleOf(Holding(0m, 0.00m, 0.00m, [Buy(100m, 1_000m, accountId: 1), Sell(100m, 1_100m, accountId: 2)]));

        sale.Matches.ShouldBe([new QuickSaleMatch(1, 2, 100m)]);
    }
}
