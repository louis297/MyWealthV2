using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.FifExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R16–R18: the CV amount of one tax holding.</summary>
public class FifIncomeCvTests
{
    private static FifCvFigures Cv(TaxHolding holding)
    {
        var cv = IncomeOf(holding).Cv.ShouldNotBeNull();
        return new FifCvFigures(cv.OpeningValue, cv.ClosingValue, cv.Sales, cv.Dividends, cv.Purchases, cv.Amount);
    }

    private sealed record FifCvFigures(decimal Opening, decimal Closing, decimal Sales, decimal Dividends, decimal Purchases, decimal Amount);

    [Test]
    public void S1Page14_ThreeCompanies_2000_10000_AndMinus20000()
    {
        Compute(
            [
                Holding(1m, 100_000m, 102_000m, [], 1),
                Holding(1m, 100_000m, 110_000m, [], 2),
                Holding(1m, 100_000m, 80_000m, [], 3)
            ])
            .Holdings.Select(income => income.Cv.ShouldNotBeNull().Amount).ShouldBe([2_000m, 10_000m, -20_000m]);
    }

    [Test]
    public void S2Example1_Jax_4125In2023()
    {
        IncomeOf(Holding(1_000m, 55_000.00m, 59_125.00m, [], taxYear: 2023), 2023).Cv.ShouldNotBeNull().Amount.ShouldBe(4_125.00m);
    }

    [TestCase(115_000, 10_000, 25_000)]
    [TestCase(102_000, 1_000, 3_000)]
    [TestCase(75_000, 10_000, -15_000)]
    public void S8Examples1To3(decimal closing, decimal dividend, decimal amount)
    {
        Cv(Holding(1_000m, 100_000m, closing, [Dividend(dividend)])).Amount.ShouldBe(amount);
    }

    [Test]
    public void S8Example4_17000OverTwoCompanies()
    {
        var incomes = Compute(
        [
            Holding(10_000m, 30_000m, 48_000m, [Buy(1_000m, 4_000m), Buy(4_000m, 20_000m), Dividend(1_000m), Sell(3_000m, 15_000m)], 1),
            Holding(1_000m, 50_000m, 55_000m, [Dividend(2_000m)], 2)
        ]).Holdings;

        incomes.Select(income => income.Cv.ShouldNotBeNull().Amount).ShouldBe([10_000m, 7_000m]);
    }

    [Test]
    public void Example2_TheFiguresOfTheSpec()
    {
        Cv(Example2()).ShouldBe(new FifCvFigures(20_000.00m, 23_100.00m, 19_000.00m, 1_360.00m, 18_700.00m, 4_760.00m));
    }

    [TestCase(2025, 752.78)]
    [TestCase(2026, 1_740.87)]
    public void Example1_TheWorkedExampleOfTaxYearEvents(int taxYear, decimal amount)
    {
        var year = TaxWorkedExample.ValuedYear(taxYear);

        FifIncome.Compute(taxYear, year.Holdings, 2).Holdings.ShouldHaveSingleItem().Cv.ShouldNotBeNull().Amount.ShouldBe(amount);
    }

    [Test]
    public void ADividendCountsAtItsGrossPlusTheImputationCredit_AndTheTaxesWithheldAreNotAddedAgain()
    {
        var cv = Cv(Holding(100m, 1_000.00m, 1_000.00m, [Dividend(59.15m, imputationCredit: 4.00m, foreignTax: 8.87m, rwt: 10.65m)]));

        (cv.Dividends, cv.Amount).ShouldBe((63.15m, 63.15m));
    }

    [Test]
    public void AReinvestedDividend_IsAGainAndACost()
    {
        var cv = Cv(Holding(100m, 1_000.00m, 1_037.63m, [Dividend(58.80m), Buy(0.8m, 37.63m)]));

        (cv.Dividends, cv.Purchases, cv.Amount).ShouldBe((58.80m, 37.63m, 58.80m));
    }

    [Test]
    public void FeesAreInTheCost_AndOffTheProceeds()
    {
        var cv = Cv(Holding(100m, 1_000.00m, 1_000.00m, [BuyWithFee(10m, 100.00m, 10.00m), SellWithFee(10m, 150.00m, 10.00m)]));

        (cv.Purchases, cv.Sales, cv.Amount).ShouldBe((110.00m, 140.00m, 30.00m));
    }

    [Test]
    public void AnArrivalIsACost_AndADepartureAGain()
    {
        var cv = Cv(Holding(0m, 0.00m, 0.00m, [Arrive(100m, 5_000.00m), Depart(100m, 5_400.00m)]));

        (cv.Purchases, cv.Sales, cv.Amount).ShouldBe((5_000.00m, 5_400.00m, 400.00m));
    }

    [Test]
    public void AMoveAReorganisationAndACorrection_AddNothing()
    {
        Cv(Holding(100m, 1_000.00m, 1_500.00m, [Move(), Split(100m), Correct(10m)])).ShouldBe(new FifCvFigures(1_000.00m, 1_500.00m, 0.00m, 0.00m, 0.00m, 500.00m));
    }
}
