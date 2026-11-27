using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R6–R14, R16, R17: which Transactions give which tax events, in which order.</summary>
public class TaxLedgerTests
{
    [Test]
    public void WorkedExample_WithoutTheTransfer_GivesTheEventsInTheOrderOfTheLedger()
    {
        var events = TaxLedger.Build([T1, T2, T3, T4, T5, T6, T7, T8, T9, T12]);

        events.Select(e => (e.Ordinal, e.Kind, e.TransactionId, e.Quantity)).ShouldBe([
            (0, TaxEventKind.Acquisition, 1, 100m),
            (1, TaxEventKind.Acquisition, 2, 40m),
            (2, TaxEventKind.Disposal, 3, -30m),
            (3, TaxEventKind.Dividend, 4, 0m),
            (4, TaxEventKind.Reorganisation, 7, 40m),
            (5, TaxEventKind.Reorganisation, 8, 70m),
            (6, TaxEventKind.Dividend, 9, 0m),
            (7, TaxEventKind.Acquisition, 9, 0.8m),
            (8, TaxEventKind.Acquisition, 12, 20m)
        ]);
        events.ShouldAllBe(e => e.InstrumentId == Aaa);
    }

    [Test]
    public void TheFactsInAnyOrder_GiveTheSameEvents()
    {
        var ordered = TaxLedger.Build([T1, T2, T3, T4, T7, T8, T9, T12]);
        var shuffled = TaxLedger.Build([T12, T9, T3, T1, T8, T4, T2, T7]);

        shuffled.Select(e => (e.Ordinal, e.TransactionId, e.Kind)).ShouldBe(ordered.Select(e => (e.Ordinal, e.TransactionId, e.Kind)));
    }

    [Test]
    public void ABuy_IsAnAcquisition_WithThePrincipalAndTheFeeOfItsAccount()
    {
        var buy = TaxLedger.Build([T1]).ShouldHaveSingleItem();

        buy.Kind.ShouldBe(TaxEventKind.Acquisition);
        buy.LedgerType.ShouldBe(TransactionType.Buy);
        (buy.TransactionId, buy.TransactionPublicId, buy.BookedAt).ShouldBe((1, IdOf(1), T1.BookedAt));
        (buy.AccountId, buy.CustodyMode).ShouldBe((A1, CustodyMode.Custodial));
        buy.Trade.ShouldBe(new TaxTradeFigures("USD", 5_000.00m, 5.00m));
        buy.Changes.ShouldBe([new TaxQuantityChange(A1, CustodyMode.Custodial, 100m)]);
        (buy.Dividend, buy.CarriedCost, buy.Move).ShouldBe((null, null, null));
    }

    [Test]
    public void ABuyOnAnNzdAccount_HasItsFiguresInNzd() =>
        TaxLedger.Build([T2]).ShouldHaveSingleItem().Trade.ShouldBe(new TaxTradeFigures("NZD", 3_640.00m, 7.00m));

    [Test]
    public void ASell_IsADisposal_WithItsQuantityNegative()
    {
        var sell = TaxLedger.Build([T3]).ShouldHaveSingleItem();

        (sell.Kind, sell.Quantity).ShouldBe((TaxEventKind.Disposal, -30m));
        sell.Trade.ShouldBe(new TaxTradeFigures("USD", 1_590.00m, 5.00m));
        sell.Changes.ShouldBe([new TaxQuantityChange(A1, CustodyMode.Custodial, -30m)]);
    }

    [Test]
    public void ATradeWithNoFeeLeg_HasAFeeOfZero() =>
        TaxLedger.Build([Fact(20, Utc(2025, 6, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(20, A2, 1m, 55m)])])
            .ShouldHaveSingleItem().Trade.ShouldBe(new TaxTradeFigures("NZD", 100.00m, 0m));

    [Test]
    public void ADividend_IsOneEvent_WithTheGrossTheTaxesAndTheCreditOfItsDetail()
    {
        var dividend = TaxLedger.Build([T4]).ShouldHaveSingleItem();

        (dividend.Kind, dividend.Quantity, dividend.InstrumentId, dividend.AccountId).ShouldBe((TaxEventKind.Dividend, 0m, Aaa, A1));
        dividend.Changes.ShouldBeEmpty();
        dividend.Dividend.ShouldBe(new TaxDividendFigures("USD", 70m, DividendTaxSource.Foreign, 35.00m, 5.25m, 6.30m, 0m, 0m));
        dividend.Trade.ShouldBeNull();
    }

    [Test]
    public void ADividendWithNrwtAndACredit_GivesThemApart()
    {
        var fact = Fact(30, Utc(2025, 8, 1, 0, 0), TransactionType.Dividend,
            [Cash(A2, CashLegRole.Principal, 100.00m), Cash(A2, CashLegRole.Tax, -15.00m, TaxKind.Nrwt)],
            [],
            dividend: new LedgerDividend(Aaa, 50m, DividendTaxSource.NewZealand, 38.89m));

        TaxLedger.Build([fact]).ShouldHaveSingleItem().Dividend
            .ShouldBe(new TaxDividendFigures("NZD", 50m, DividendTaxSource.NewZealand, 100.00m, 0m, 0m, 15.00m, 38.89m));
    }

    [Test]
    public void AReinvestedDividend_IsTheDividendThenAnAcquisitionOfTheReinvestedAmount()
    {
        var events = TaxLedger.Build([T9]);

        events.Select(e => e.Kind).ShouldBe([TaxEventKind.Dividend, TaxEventKind.Acquisition]);
        events[0].Quantity.ShouldBe(0m);
        events[1].Quantity.ShouldBe(0.8m);
        events[1].Trade.ShouldBe(new TaxTradeFigures("USD", 22.40m, 0m));
        events[1].Changes.ShouldBe([new TaxQuantityChange(A1, CustodyMode.Custodial, 0.8m)]);
        events.ShouldAllBe(e => e.TransactionId == 9);
    }

    [Test]
    public void AReversedTransactionAndItsReversal_GiveNothing() =>
        TaxLedger.Build([T5, T6]).ShouldBeEmpty();

    [Test]
    public void AReversalAcrossAYearEnd_GivesNothingInEitherYear()
    {
        var buy = Fact(40, Utc(2025, 3, 30, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(40, A2, 2m, 50m)], reversed: true);
        var reversal = Fact(41, Utc(2025, 4, 2, 0, 0), TransactionType.Reversal, [Cash(A2, CashLegRole.Principal, 100.00m)], [Security(41, A2, -2m, -50m)]);

        TaxLedger.Build([buy, reversal, T1]).Select(e => e.TransactionId).ShouldBe([1]);
    }

    [Test]
    public void TwoTransactionsWithOneBookedAt_AreTakenInTheOrderWritten()
    {
        var at = Utc(2025, 6, 1, 0, 0);
        var first = Fact(50, at, TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(50, A2, 2m, 50m)]);
        var second = Fact(51, at, TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 60.00m)], [Security(51, A2, -1m, -25m)]);

        TaxLedger.Build([second, first]).Select(e => (e.TransactionId, e.Kind)).ShouldBe([(50, TaxEventKind.Acquisition), (51, TaxEventKind.Disposal)]);
    }

    [Test]
    public void OpeningHoldingsWithThreeLegs_GiveThreeArrivals_InTheOrderOfTheLegs()
    {
        var fact = Fact(60, Utc(2024, 1, 5, 0, 0), TransactionType.OpeningHoldings, [],
            [Security(62, A2, 5m, 900m, instrumentId: 12), Security(61, A2, 10m, 500m, instrumentId: 11), Security(63, A2, 2m, 40m, instrumentId: 13)]);

        var events = TaxLedger.Build([fact]);

        events.Select(e => (e.Kind, e.InstrumentId, e.Quantity)).ShouldBe([
            (TaxEventKind.Arrival, 11, 10m),
            (TaxEventKind.Arrival, 12, 5m),
            (TaxEventKind.Arrival, 13, 2m)
        ]);
        events[0].CarriedCost.ShouldBe(new TaxCarriedCost(500m, "USD", null));
    }

    [Test]
    public void AStockIn_IsAnArrival_WithItsCostAndTheStatedAcquisitionDate()
    {
        var fact = Fact(70, Utc(2025, 8, 4, 0, 0), TransactionType.StockIn, [], [Security(70, A1, 25m, 700m)],
            inKind: new LedgerInKindTransfer(true, new DateOnly(2021, 3, 18)));

        var arrival = TaxLedger.Build([fact]).ShouldHaveSingleItem();

        (arrival.Kind, arrival.Quantity).ShouldBe((TaxEventKind.Arrival, 25m));
        arrival.CarriedCost.ShouldBe(new TaxCarriedCost(700m, "USD", new DateOnly(2021, 3, 18)));
        arrival.Trade.ShouldBeNull();
    }

    [Test]
    public void AStockOut_IsADeparture()
    {
        var departure = TaxLedger.Build([T10]).ShouldHaveSingleItem();

        (departure.Kind, departure.Quantity, departure.AccountId).ShouldBe((TaxEventKind.Departure, -80m, A2));
        departure.Changes.ShouldBe([new TaxQuantityChange(A2, CustodyMode.External, -80m)]);
        departure.CarriedCost.ShouldBeNull();
    }

    [TestCase(TransactionType.Split, 40)]
    [TestCase(TransactionType.Split, -50)]
    [TestCase(TransactionType.Bonus, 20)]
    [TestCase(TransactionType.Scrip, 3)]
    public void ASplitABonusAndAScrip_AreReorganisations(TransactionType type, decimal quantity)
    {
        var fact = Fact(80, Utc(2025, 7, 1, 0, 0), type, [], [Security(80, A2, quantity, 0m)]);

        var reorganisation = TaxLedger.Build([fact]).ShouldHaveSingleItem();

        (reorganisation.Kind, reorganisation.LedgerType, reorganisation.Quantity).ShouldBe((TaxEventKind.Reorganisation, type, quantity));
        (reorganisation.Trade, reorganisation.CarriedCost).ShouldBe((null, null));
    }

    [Test]
    public void AnAdjustmentOfAHolding_IsACorrection_WithItsCostChange()
    {
        var fact = Fact(90, Utc(2025, 9, 1, 0, 0), TransactionType.Adjustment,
            [Cash(A1, CashLegRole.Principal, 12.00m)], [Security(90, A1, -2m, -110m)]);

        var correction = TaxLedger.Build([fact]).ShouldHaveSingleItem();

        (correction.Kind, correction.Quantity).ShouldBe((TaxEventKind.Correction, -2m));
        correction.CarriedCost.ShouldBe(new TaxCarriedCost(-110m, "USD", null));
        correction.Trade.ShouldBeNull();
    }

    [Test]
    public void ACostOnlyAdjustment_IsACorrectionOfQuantityZero() =>
        TaxLedger.Build([Fact(91, Utc(2025, 9, 1, 0, 0), TransactionType.Adjustment, [], [Security(91, A1, 0m, 25m)])])
            .ShouldHaveSingleItem().Quantity.ShouldBe(0m);

    [Test]
    public void AnAdjustmentOfCash_GivesNothing() =>
        TaxLedger.Build([Fact(92, Utc(2025, 9, 1, 0, 0), TransactionType.Adjustment, [Cash(A1, CashLegRole.Principal, 12.00m)], [])])
            .ShouldBeEmpty();

    [TestCase(TransactionType.TransferIn)]
    [TestCase(TransactionType.TransferOut)]
    [TestCase(TransactionType.Interest)]
    [TestCase(TransactionType.Opening)]
    [TestCase(TransactionType.CloseOut)]
    [TestCase(TransactionType.InternalTransfer)]
    [TestCase(TransactionType.FxConversion)]
    public void ACashOnlyType_GivesNothing(TransactionType type) =>
        TaxLedger.Build([Fact(100, Utc(2025, 9, 1, 0, 0), type, [Cash(A1, CashLegRole.Principal, 12.00m)], [])]).ShouldBeEmpty();
}
