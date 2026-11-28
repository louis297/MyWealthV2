using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold §5, R24, R25: <see cref="CustomerTaxYear.Compute"/> carries the tax cost, the threshold test
/// and the notice <c>CarriedCost</c>.</summary>
public class CustomerTaxYearCostTests
{
    private static readonly TaxTreatment Exempt = new(FifTreatment.ExemptAsxListed, 2025, null, "AU");

    [Test]
    public void Example1_TheTaxCostOfTheHoldingAndOfEachEvent()
    {
        var holding = Year(2026).Holdings.ShouldHaveSingleItem();

        holding.Cost.ShouldBe(new TaxCost(TaxCostStatus.Known, 12_355.70m, 10_792.33m, null));
        holding.Events.Select(e => e.Cost).ShouldBe(
        [
            new TaxEventCost(-2_647.65m, 9_708.05m, null),
            new TaxEventCost(0.00m, 9_708.05m, null),
            new TaxEventCost(0.00m, 9_708.05m, null),
            new TaxEventCost(0.00m, 9_708.05m, null),
            new TaxEventCost(0.00m, 9_708.05m, null),
            new TaxEventCost(37.63m, 9_745.68m, null),
            new TaxEventCost(0.00m, 9_745.68m, null),
            new TaxEventCost(1_046.65m, 10_792.33m, null)
        ]);
    }

    [Test]
    public void TheClosingCostOf2025_IsTheOpeningCostOf2026()
    {
        var closing = Year(2025).Holdings.ShouldHaveSingleItem().Cost;
        var opening = Year(2026).Holdings.ShouldHaveSingleItem().Cost;

        closing.ShouldBe(new TaxCost(TaxCostStatus.Known, 0.00m, 12_355.70m, null));
        opening.Opening.ShouldBe(closing.Closing);
    }

    [Test]
    public void Example1_TheThresholdTestOfEachYear()
    {
        var test2025 = Year(2025).Threshold;
        test2025.Peak.ShouldBe(new ThresholdPeak(12_355.70m, Utc(2025, 3, 31, 9, 30), Aaa, test2025.Peak.Event));
        test2025.Peak.Event!.TransactionId.ShouldBe(2);
        (test2025.Amount, test2025.Opening, test2025.Closing, test2025.Outcome, test2025.Complete)
            .ShouldBe((50_000.00m, 0.00m, 12_355.70m, ThresholdOutcome.NotAbove, true));

        var test2026 = Year(2026).Threshold;
        test2026.Peak.ShouldBe(new ThresholdPeak(12_355.70m, TaxYear.Start(2026), null, null));
        (test2026.Opening, test2026.Closing, test2026.Outcome).ShouldBe((12_355.70m, 10_792.33m, ThresholdOutcome.NotAbove));
    }

    [Test]
    public void AnExemptHolding_NeedsNoTaxCost()
    {
        var holding = Year(2026, treatments: new Dictionary<int, TaxTreatment> { [Aaa] = Exempt }).Holdings.ShouldHaveSingleItem();

        holding.Cost.ShouldBe(TaxCost.NotRequired);
        holding.Events.ShouldAllBe(e => e.Cost == null);
        // Attributing, the same holding has one.
        Year(2026).Holdings.ShouldHaveSingleItem().Cost.Status.ShouldBe(TaxCostStatus.Known);
    }

    [Test]
    public void AnUnknownCost_NamesItsFirstCause_FromAnEarlierTaxYear()
    {
        var facts = new[]
        {
            Fact(1, Utc(2024, 5, 20, 0, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -100.00m)], [Security(1, A1, 10m, 100.00m)]),
            Fact(2, Utc(2025, 6, 2, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(2, A2, 10m, 57.00m)])
        };

        var year = Year(2026, facts, rates: []);

        var holding = year.Holdings.ShouldHaveSingleItem();
        holding.Cost.ShouldBe(new TaxCost(
            TaxCostStatus.Unknown, null, null, new TaxCostUnknown(TaxCostUnknownCause.NoRate, 0, Utc(2024, 5, 20, 0, 0), IdOf(1))));
        holding.Events.ShouldHaveSingleItem().Cost.ShouldBe(new TaxEventCost(null, null, null));
        year.Threshold.Incomplete.ShouldBe([new ThresholdIncomplete(ThresholdIncompleteReason.CostUnknown, Aaa)]);
    }

    [Test]
    public void AnIncompleteTest_LeavesTheTaxHoldingAndTheTaxYearComplete()
    {
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment> { [(Aaa, 2025)] = FifTreatment.ExemptOther };

        var year = ValuedYear(2026, earlierTreatments: earlier);

        year.Threshold.Incomplete.ShouldBe([new ThresholdIncomplete(ThresholdIncompleteReason.TreatmentChanged, Aaa)]);
        year.Threshold.Outcome.ShouldBe(ThresholdOutcome.Unknown);
        (year.Complete, year.IncompleteCount, year.Holdings.ShouldHaveSingleItem().Complete).ShouldBe((true, 0, true));
    }

    [Test]
    public void AnArrivalOfTheYear_HasItsCostAndRate_AndTheNoticeCarriedCost()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 8, 4, 3, 0), TransactionType.StockIn, [], [Security(1, A1, 60m, 2_400.00m)],
                inKind: new LedgerInKindTransfer(false, new DateOnly(2024, 6, 14)))
        };

        var holding = Year(2026, facts, rates: [Rate(2024, 6, 14, 1.6300m), Rate(2025, 8, 4, 1.7000m)]).Holdings.ShouldHaveSingleItem();

        holding.Events.ShouldHaveSingleItem().Cost.ShouldBe(new TaxEventCost(3_912.00m, 3_912.00m, Rate(2024, 6, 14, 1.6300m)));
        holding.Notices.ShouldBe([TaxNotice.ArrivalAtMarketValue, TaxNotice.CarriedCost]);
    }

    [Test]
    public void AnArrivalOfAnEarlierYear_StillHeld_GivesTheNoticeCarriedCost()
    {
        var facts = new[] { Fact(1, Utc(2024, 8, 4, 3, 0), TransactionType.OpeningHoldings, [], [Security(1, A2, 60m, 2_400.00m)]) };

        var holding = Year(2026, facts, rates: [Rate(2024, 8, 4, 1.6300m)]).Holdings.ShouldHaveSingleItem();

        holding.Cost.Opening.ShouldBe(3_912.00m);
        holding.Notices.ShouldBe([TaxNotice.ExternalFigures, TaxNotice.CarriedCost]);
    }

    [Test]
    public void AnArrivalBeforeTheQuantityLastLeft0_GivesNoNotice()
    {
        var facts = new[]
        {
            Fact(1, Utc(2023, 8, 4, 3, 0), TransactionType.OpeningHoldings, [], [Security(1, A2, 60m, 2_400.00m)]),
            Fact(2, Utc(2024, 5, 1, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 3_000.00m)], [Security(2, A2, -60m, -2_400.00m)]),
            Fact(3, Utc(2024, 6, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -3_000.00m)], [Security(3, A2, 60m, 1_700.00m)])
        };

        var holding = Year(2026, facts, rates: [Rate(2023, 8, 4, 1.6300m)]).Holdings.ShouldHaveSingleItem();

        holding.Cost.Opening.ShouldBe(3_000.00m);
        holding.Notices.ShouldBe([TaxNotice.ExternalFigures]);
        // Held from the arrival instead, the notice is there.
        Year(2026, [facts[0]], rates: [Rate(2023, 8, 4, 1.6300m)]).Holdings.ShouldHaveSingleItem().Notices
            .ShouldBe([TaxNotice.ExternalFigures, TaxNotice.CarriedCost]);
    }

    [Test]
    public void AnArrivalOfAnExemptHolding_GivesNoNoticeCarriedCost()
    {
        var facts = new[] { Fact(1, Utc(2025, 8, 4, 3, 0), TransactionType.OpeningHoldings, [], [Security(1, A2, 60m, 2_400.00m)]) };
        var treatments = new Dictionary<int, TaxTreatment> { [Aaa] = Exempt };

        Year(2026, facts, rates: [], treatments: treatments).Holdings.ShouldHaveSingleItem().Notices
            .ShouldBe([TaxNotice.ExternalFigures, TaxNotice.ArrivalAtMarketValue]);
        // The same arrival of an attributing holding has it.
        Year(2026, facts, rates: [Rate(2025, 8, 4, 1.6300m)]).Holdings.ShouldHaveSingleItem().Notices
            .ShouldBe([TaxNotice.ExternalFigures, TaxNotice.ArrivalAtMarketValue, TaxNotice.CarriedCost]);
    }
}
