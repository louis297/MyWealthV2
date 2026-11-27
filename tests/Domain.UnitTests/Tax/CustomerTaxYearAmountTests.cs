using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R18–R21: the NZD amount of each tax event, and why it has none.</summary>
public class CustomerTaxYearAmountTests
{
    [Test]
    public void WorkedExample_2025_ConvertsAUsdLeg_AndUsesAnNzdLegAsBooked()
    {
        var year = Year(2025);

        (year.TaxYear, year.From, year.To).ShouldBe((2025, TaxYear.Start(2025), TaxYear.End(2025)));
        var events = year.Holdings.ShouldHaveSingleItem().Events;
        events.Select(e => (e.Sequence, e.Event.TransactionId, e.Status, e.Principal, e.Fee, e.Amount)).ShouldBe([
            (1, 1, TaxAmountStatus.Converted, 8_700.00m, 8.70m, 8_708.70m),
            (2, 2, TaxAmountStatus.Stated, 3_640.00m, 7.00m, 3_647.00m)
        ]);
        events[0].Fx.ShouldBe(Rate(2025, 3, 10, 1.7400m));
        events[1].Fx.ShouldBeNull();
    }

    [Test]
    public void WorkedExample_2026_HasTheAmountsOfTheExample()
    {
        var events = Year(2026).Holdings.ShouldHaveSingleItem().Events;

        events.Select(e => (e.Sequence, e.Event.Kind, e.Status, e.Amount)).ShouldBe([
            (1, TaxEventKind.Disposal, TaxAmountStatus.Converted, 2_789.60m),
            (2, TaxEventKind.Dividend, TaxAmountStatus.Converted, 59.15m),
            (3, TaxEventKind.Reorganisation, TaxAmountStatus.None, null),
            (4, TaxEventKind.Reorganisation, TaxAmountStatus.None, null),
            (5, TaxEventKind.Dividend, TaxAmountStatus.Converted, 58.80m),
            (6, TaxEventKind.Acquisition, TaxAmountStatus.Converted, 37.63m),
            (7, TaxEventKind.Move, TaxAmountStatus.None, null),
            (8, TaxEventKind.Acquisition, TaxAmountStatus.Converted, 1_046.65m)
        ]);
        (events[0].Principal, events[0].Fee, events[0].Fx).ShouldBe((2_798.40m, 8.80m, Rate(2025, 3, 31, 1.7600m)));
        events[1].Dividend.ShouldBe(new TaxDividendAmounts(70m, DividendTaxSource.Foreign, 59.15m, 8.87m, 10.65m, 0m, 0m));
        events[1].Fx.ShouldBe(Rate(2025, 5, 20, 1.6900m));
        events[4].Dividend.ShouldBe(new TaxDividendAmounts(140m, DividendTaxSource.Foreign, 58.80m, 8.82m, 10.58m, 0m, 0m));
        (events[5].Principal, events[5].Fee).ShouldBe((37.63m, 0m));
        (events[7].Principal, events[7].Fee).ShouldBe((1_038.00m, 8.65m));
        (events[2].Fx, events[2].Principal, events[2].Dividend).ShouldBe((null, null, null));
    }

    [Test]
    public void EachFigure_IsConvertedOnItsOwn_AndTheAmountIsTheirSum()
    {
        // 10.01 × 1.5 = 15.015 → 15.02 and 0.01 × 1.5 = 0.015 → 0.02: the amount is 15.04, not Round(10.02 × 1.5) = 15.03.
        var buy = Fact(20, Utc(2025, 6, 2, 0, 0), TransactionType.Buy,
            [Cash(A1, CashLegRole.Principal, -10.01m), Cash(A1, CashLegRole.Fee, -0.01m)], [Security(20, A1, 1m, 10.02m)]);

        var taxEvent = Year(2026, [buy], [Rate(2025, 6, 2, 1.5000m)]).Holdings.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();

        (taxEvent.Principal, taxEvent.Fee, taxEvent.Amount).ShouldBe((15.02m, 0.02m, 15.04m));
    }

    [Test]
    public void ADisposal_IsThePrincipalLessTheFee()
    {
        var sell = Fact(21, Utc(2025, 6, 2, 0, 0), TransactionType.Sell,
            [Cash(A2, CashLegRole.Principal, 500.00m), Cash(A2, CashLegRole.Fee, -10.00m)], [Security(21, A2, -1m, -400m)]);

        var taxEvent = Year(2026, [Opening(A2, 5m), sell]).Holdings.ShouldHaveSingleItem().Events.Last();

        (taxEvent.Status, taxEvent.Principal, taxEvent.Fee, taxEvent.Amount).ShouldBe((TaxAmountStatus.Stated, 500.00m, 10.00m, 490.00m));
    }

    [Test]
    public void ADividendWithACredit_ConvertsTheCredit()
    {
        var dividend = Fact(22, Utc(2025, 6, 2, 0, 0), TransactionType.Dividend,
            [Cash(A1, CashLegRole.Principal, 100.00m), Cash(A1, CashLegRole.Tax, -15.00m, TaxKind.Nrwt)], [],
            dividend: new LedgerDividend(Aaa, 50m, DividendTaxSource.NewZealand, 20.00m));

        var taxEvent = Year(2026, [dividend], [Rate(2025, 6, 2, 1.5000m)]).Holdings.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();

        taxEvent.Dividend.ShouldBe(new TaxDividendAmounts(50m, DividendTaxSource.NewZealand, 150.00m, 0m, 0m, 22.50m, 30.00m));
        taxEvent.Amount.ShouldBe(150.00m);
    }

    [Test]
    public void NoRate_LeavesEveryFigureNull()
    {
        var events = Year(2026, rates: []).Holdings.ShouldHaveSingleItem().Events;

        var disposal = events[0];
        (disposal.Status, disposal.Amount, disposal.Principal, disposal.Fee, disposal.Fx).ShouldBe((TaxAmountStatus.NoRate, null, null, null, null));
        var dividend = events[1];
        dividend.Status.ShouldBe(TaxAmountStatus.NoRate);
        dividend.Dividend.ShouldBe(new TaxDividendAmounts(70m, DividendTaxSource.Foreign, null, null, null, null, null));
        dividend.Amount.ShouldBeNull();
    }

    [Test]
    public void ARateFiveDaysOld_IsStale_AndKeepsTheRowItFound()
    {
        var buy = Fact(23, Utc(2025, 6, 6, 0, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -100.00m)], [Security(23, A1, 1m, 100m)]);
        var row = Rate(2025, 6, 1, 1.5000m);

        var taxEvent = Year(2026, [buy], [row]).Holdings.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();

        (taxEvent.Status, taxEvent.Amount, taxEvent.Principal, taxEvent.Fx).ShouldBe((TaxAmountStatus.StaleRate, null, null, row));
    }

    [Test]
    public void ARateFourDaysOld_IsUsed()
    {
        var buy = Fact(24, Utc(2025, 6, 5, 23, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -100.00m)], [Security(24, A1, 1m, 100m)]);

        Year(2026, [buy], [Rate(2025, 6, 1, 1.5000m)]).Holdings.ShouldHaveSingleItem().Events.ShouldHaveSingleItem()
            .Amount.ShouldBe(150.00m);
    }

    [Test]
    public void ARateIsReadForTheUtcDateOfTheBookedAt()
    {
        // T3 is on 1 April in Auckland and on 31 March in UTC: the rate of 31 March.
        var taxEvent = Year(2026).Holdings.ShouldHaveSingleItem().Events[0];

        taxEvent.Fx!.AsOfDate.ShouldBe(new DateOnly(2025, 3, 31));
    }

    [Test]
    public void AnArrival_HasTheMarketValueOfItsDay()
    {
        var arrival = Fact(25, Utc(2025, 8, 4, 0, 0), TransactionType.StockIn, [], [Security(25, A2, 25m, 700m)]);
        var events = TaxLedger.Build([arrival]);
        var price = Price(2025, 8, 4, 31.20m);
        var fx = Rate(2025, 8, 4, 1.6800m);
        var line = new ValuationLine(Aaa, 25m, 25m, ValuationLineStatus.Valued, price, 780.00m, fx, 1_310.40m);

        var taxEvent = CustomerTaxYear.Compute(2026, events, Treatments, RatesFor(events, []),
                new TaxYearValues([], [], new Dictionary<int, ValuationLine> { [0] = line }), 2)
            .Holdings.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();

        (taxEvent.Status, taxEvent.Amount, taxEvent.Price, taxEvent.Fx).ShouldBe((TaxAmountStatus.MarketValue, 1_310.40m, price, fx));
        (taxEvent.Principal, taxEvent.Fee, taxEvent.Dividend).ShouldBe((null, null, null));
    }

    [TestCase(ValuationLineStatus.NoPrice, TaxAmountStatus.NoPrice)]
    [TestCase(ValuationLineStatus.StalePrice, TaxAmountStatus.StalePrice)]
    [TestCase(ValuationLineStatus.NoRate, TaxAmountStatus.NoRate)]
    [TestCase(ValuationLineStatus.StaleRate, TaxAmountStatus.StaleRate)]
    public void ADepartureThatIsNotValued_HasNoAmount_AndTheStatusOfItsLine(ValuationLineStatus lineStatus, TaxAmountStatus status)
    {
        var departure = Fact(26, Utc(2025, 8, 4, 0, 0), TransactionType.StockOut, [], [Security(26, A2, -25m, -700m)]);
        var events = TaxLedger.Build([Opening(A2, 25m), departure]);
        var line = new ValuationLine(Aaa, 25m, 25m, lineStatus, null, null, null, null);

        var taxEvent = CustomerTaxYear.Compute(2026, events, Treatments, RatesFor(events, []),
                new TaxYearValues([], [], new Dictionary<int, ValuationLine> { [1] = line }), 2)
            .Holdings.ShouldHaveSingleItem().Events.Last();

        (taxEvent.Status, taxEvent.Amount).ShouldBe((status, null));
    }

    /// <summary>An <c>OpeningHoldings</c> long before tax year 2026, so that the holding has a quantity.</summary>
    private static LedgerFact Opening(int accountId, decimal quantity) =>
        Fact(1_000 + accountId, Utc(2020, 1, 1, 0, 0), TransactionType.OpeningHoldings, [], [Security(1_000 + accountId, accountId, quantity, 0m)]);
}
