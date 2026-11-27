using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-year-events R26–R29: the treatment, why a tax holding is incomplete, and its notices.</summary>
public class CustomerTaxYearReasonTests
{
    private static readonly TaxTreatment Attributing = new(FifTreatment.Attributing, 2025, null, "US");

    [Test]
    public void WorkedExample_IsComplete_InBothYears_WithItsNotices()
    {
        var year2025 = ValuedYear(2025);
        var year2026 = ValuedYear(2026);

        (year2025.Complete, year2025.IncompleteCount, year2026.Complete, year2026.IncompleteCount).ShouldBe((true, 0, true, 0));
        var holding = year2026.Holdings.ShouldHaveSingleItem();
        (holding.Complete, holding.Incomplete.Count).ShouldBe((true, 0));
        holding.Notices.ShouldBe([TaxNotice.ExternalFigures, TaxNotice.Move]);
        year2025.Holdings.ShouldHaveSingleItem().Notices.ShouldBe([TaxNotice.ExternalFigures]);
    }

    [Test]
    public void AHolding_CarriesItsTreatment() =>
        ValuedYear(2026).Holdings.ShouldHaveSingleItem().Treatment.ShouldBe(Attributing);

    [Test]
    public void AnInstrumentWithNoTreatment_IsNotClassified_AndIncomplete()
    {
        var year = ValuedYear(2026, treatments: new Dictionary<int, TaxTreatment>());

        var holding = year.Holdings.ShouldHaveSingleItem();
        holding.Treatment.ShouldBe(TaxTreatment.NotClassified());
        holding.Incomplete.ShouldBe([TaxIncompleteReason.NotClassified]);
        (holding.Complete, year.Complete, year.IncompleteCount).ShouldBe((false, false, 1));
    }

    [Test]
    public void NoPriceAtEitherEnd_GivesBothReasons()
    {
        var events = TaxLedger.Build(Facts);

        var holding = Year(2026, values: ValuesOf(2026, events, prices: [])).Holdings.ShouldHaveSingleItem();

        holding.Incomplete.ShouldBe([TaxIncompleteReason.OpeningNotValued, TaxIncompleteReason.ClosingNotValued]);
    }

    [Test]
    public void AnEventWithNoRate_IsAmountMissing()
    {
        var events = TaxLedger.Build(Facts);

        var holding = Year(2026, rates: [], values: ValuesOf(2026, events)).Holdings.ShouldHaveSingleItem();

        holding.Incomplete.ShouldBe([TaxIncompleteReason.AmountMissing]);
    }

    [Test]
    public void AnArrivalThatIsNotValued_IsAmountMissing_WithItsNotice()
    {
        var arrival = Fact(20, Utc(2025, 8, 4, 0, 0), TransactionType.StockIn, [], [Security(20, A1, 25m, 700m)]);
        var events = TaxLedger.Build([arrival]);
        var lines = new Dictionary<int, ValuationLine> { [0] = new(Aaa, 25m, 25m, ValuationLineStatus.NoPrice, null, null, null, null) };

        var holding = Year(2026, [arrival], values: ValuesOf(2026, events, eventLines: lines)).Holdings.ShouldHaveSingleItem();

        holding.Incomplete.ShouldBe([TaxIncompleteReason.AmountMissing]);
        holding.Notices.ShouldBe([TaxNotice.ArrivalAtMarketValue]);
    }

    [Test]
    public void AQuantityBelowZero_IsNegativeQuantity()
    {
        var sell = Fact(20, Utc(2025, 8, 4, 0, 0), TransactionType.Sell, [Cash(A1, CashLegRole.Principal, 10.00m)], [Security(20, A1, -5m, 0m)]);
        var events = TaxLedger.Build([sell]);

        var holding = Year(2026, [sell], values: ValuesOf(2026, events)).Holdings.ShouldHaveSingleItem();

        holding.Incomplete.ShouldContain(TaxIncompleteReason.NegativeQuantity);
        holding.Incomplete.ShouldContain(TaxIncompleteReason.ClosingNotValued);
    }

    [Test]
    public void ANegativeOpening_IsNegativeQuantity()
    {
        var sell = Fact(20, Utc(2024, 8, 4, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 10.00m)], [Security(20, A2, -5m, 0m)]);

        Year(2026, [sell], treatments: new Dictionary<int, TaxTreatment> { [Aaa] = new(FifTreatment.NotForeign, 2025, null, "NZ") })
            .Holdings.ShouldHaveSingleItem().Incomplete.ShouldBe([TaxIncompleteReason.NegativeQuantity]);
    }

    [Test]
    public void NoFactorForAReorganisation_IsNoComparableQuantity()
    {
        var facts = new[]
        {
            Fact(1, Utc(2020, 1, 1, 0, 0), TransactionType.OpeningHoldings, [], [Security(1, A1, 100m, 0m)]),
            Fact(10, Utc(2025, 7, 1, 0, 0), TransactionType.Bonus, [], [Security(10, A2, 5m, 0m)])
        };

        ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem().Incomplete.ShouldBe([TaxIncompleteReason.NoComparableQuantity]);
    }

    [Test]
    public void ANewZealandHoldingWithNoPrice_IsCompleteAndNotRequired()
    {
        var events = TaxLedger.Build(Facts);

        var holding = Year(2026, values: ValuesOf(2026, events, prices: []),
                treatments: new Dictionary<int, TaxTreatment> { [Aaa] = new(FifTreatment.NotForeign, 2025, null, "NZ") })
            .Holdings.ShouldHaveSingleItem();

        (holding.Complete, holding.Opening.Status, holding.Closing.Status).ShouldBe((true, TaxValueStatus.NotRequired, TaxValueStatus.NotRequired));
    }

    [Test]
    public void AHoldingOnACustodialAccountOnly_HasNoExternalFigures()
    {
        var holding = ValuedYear(2026, [T1, T3, T12]).Holdings.ShouldHaveSingleItem();

        holding.Notices.ShouldBeEmpty();
    }

    [Test]
    public void AnExternalLegInTheOpeningQuantity_IsExternalFigures()
    {
        // T2 is on A2 and in tax year 2025; in 2026 the only events are on A1.
        ValuedYear(2026, [T1, T2, T12]).Holdings.ShouldHaveSingleItem().Notices.ShouldBe([TaxNotice.ExternalFigures]);
    }

    [Test]
    public void EachKindWithANotice_GivesItOnce()
    {
        var facts = new[]
        {
            Fact(1, Utc(2020, 1, 1, 0, 0), TransactionType.OpeningHoldings, [], [Security(1, A1, 100m, 0m)]),
            Fact(10, Utc(2025, 5, 1, 0, 0), TransactionType.StockIn, [], [Security(10, A1, 5m, 10m)]),
            Fact(11, Utc(2025, 5, 2, 0, 0), TransactionType.StockIn, [], [Security(11, A1, 6m, 10m)]),
            Fact(12, Utc(2025, 8, 2, 0, 0), TransactionType.StockOut, [], [Security(12, A1, -7m, -10m)]),
            Fact(13, Utc(2025, 9, 2, 0, 0), TransactionType.Scrip, [], [Security(13, A1, 4m, 0m)]),
            Fact(14, Utc(2025, 10, 2, 0, 0), TransactionType.Adjustment, [], [Security(14, A1, 1m, 5m)]),
            Fact(15, Utc(2025, 11, 2, 0, 0), TransactionType.Adjustment, [], [Security(15, A1, 0m, 5m)])
        };

        var holding = Year(2026, facts, values: ValuesOf(2026, TaxLedger.Build(facts))).Holdings.ShouldHaveSingleItem();

        holding.Notices.ShouldBe([TaxNotice.ArrivalAtMarketValue, TaxNotice.DepartureAtMarketValue, TaxNotice.Scrip, TaxNotice.Correction]);
    }

    [Test]
    public void ASplitAndABonus_GiveNoNotice()
    {
        var facts = new[]
        {
            Fact(1, Utc(2020, 1, 1, 0, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -1.00m)], [Security(1, A1, 100m, 0m)]),
            Fact(13, Utc(2025, 9, 2, 0, 0), TransactionType.Split, [], [Security(13, A1, 100m, 0m)]),
            Fact(14, Utc(2025, 9, 3, 0, 0), TransactionType.Bonus, [], [Security(14, A1, 20m, 0m)])
        };

        ValuedYear(2026, facts).Holdings.ShouldHaveSingleItem().Notices.ShouldBeEmpty();
    }

    [TestCase(DividendTaxSource.NewZealand, "US", true)]
    [TestCase(DividendTaxSource.Foreign, "NZ", true)]
    [TestCase(DividendTaxSource.Foreign, "US", false)]
    [TestCase(DividendTaxSource.NewZealand, "NZ", false)]
    [TestCase(DividendTaxSource.NewZealand, null, false)]
    [TestCase(DividendTaxSource.Foreign, null, false)]
    public void ADividendWhoseTaxSourceDisagreesWithTheIssuerCountry_HasANotice(DividendTaxSource source, string? country, bool notice)
    {
        var dividend = Fact(20, Utc(2025, 8, 4, 0, 0), TransactionType.Dividend, [Cash(A1, CashLegRole.Principal, 10.00m)], [],
            dividend: new LedgerDividend(Aaa, 10m, source, 0m));
        var treatments = new Dictionary<int, TaxTreatment> { [Aaa] = new(FifTreatment.Attributing, 2025, null, country) };

        var holding = Year(2026, [dividend], [Rate(2025, 8, 4, 1.6000m)], treatments: treatments).Holdings.ShouldHaveSingleItem();

        holding.Notices.Contains(TaxNotice.DividendTaxSourceDisagrees).ShouldBe(notice);
        holding.Events.ShouldHaveSingleItem().Dividend!.TaxSource.ShouldBe(source);
    }

    [Test]
    public void TwoHoldings_OneIncomplete_CountOne()
    {
        var other = Fact(20, Utc(2025, 8, 4, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -10.00m)], [Security(20, A2, 1m, 10m, instrumentId: Aaa + 1)]);
        var facts = Facts.Append(other).ToList();

        var year = ValuedYear(2026, facts);

        year.Holdings.Select(holding => (holding.InstrumentId, holding.Complete)).ShouldBe([(Aaa, true), (Aaa + 1, false)]);
        (year.Complete, year.IncompleteCount).ShouldBe((false, 1));
    }
}
