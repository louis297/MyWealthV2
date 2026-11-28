using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.ThresholdExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold R15, R20–R22: why a test is incomplete, the floor, and a treatment that changed while held.</summary>
public class ThresholdTestReasonTests
{
    private static LedgerFact NzdBuy(int id, DateTimeOffset at, decimal quantity, decimal principal, int instrumentId = Aaa) =>
        Fact(id, at, TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -principal)], [Security(id, A2, quantity, 0m, instrumentId)]);

    private static LedgerFact NzdSell(int id, DateTimeOffset at, decimal quantity, int instrumentId = Aaa) =>
        Fact(id, at, TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 1m)], [Security(id, A2, -quantity, 0m, instrumentId)]);

    private static LedgerFact UsdBuy(int id, DateTimeOffset at, decimal quantity, decimal principal, int instrumentId = Aaa) =>
        Fact(id, at, TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -principal)], [Security(id, A1, quantity, principal, instrumentId)]);

    private static IReadOnlyDictionary<int, TaxTreatment> AaaAttributing => new Dictionary<int, TaxTreatment> { [Aaa] = Attributing };

    private static List<(ThresholdIncompleteReason, int?)> Reasons(ThresholdTest test) =>
        test.Incomplete.Select(reason => (reason.Reason, reason.InstrumentId)).ToList();

    [Test]
    public void ANotClassifiedHolding_IsNotCounted_AndMakesTheTestIncomplete()
    {
        var facts = new[] { NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 1_000.00m), NzdBuy(2, Utc(2025, 5, 2, 0, 0), 10m, 70_000.00m, Bbb) };

        var test = Test(2026, facts, AaaAttributing);

        Reasons(test).ShouldBe([(ThresholdIncompleteReason.NotClassified, (int?)Bbb)]);
        test.Peak.Total.ShouldBe(1_000.00m);
        test.Complete.ShouldBeFalse();
        test.Outcome.ShouldBe(ThresholdOutcome.Unknown);
    }

    [Test]
    public void ACostUnknownAfterAnEventOfTheYear_MakesTheTestIncomplete_AndIsAStep()
    {
        var facts = new[] { NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 1_000.00m), UsdBuy(2, Utc(2025, 5, 20, 0, 0), 5m, 100.00m) };

        var test = Test(2026, facts, AaaAttributing);

        Reasons(test).ShouldBe([(ThresholdIncompleteReason.CostUnknown, (int?)Aaa)]);
        test.Steps.Select(step => (step.Event.TransactionId, step.Change, step.Total)).ShouldBe(
        [
            (1, (decimal?)1_000.00m, 1_000.00m),
            (2, null, 0.00m)
        ]);
        test.Outcome.ShouldBe(ThresholdOutcome.Unknown);
    }

    [Test]
    public void ACostUnknownAtTheOpening_MakesTheTestIncomplete()
    {
        var test = Test(2026, [UsdBuy(1, Utc(2023, 5, 20, 0, 0), 5m, 100.00m)], AaaAttributing);

        Reasons(test).ShouldBe([(ThresholdIncompleteReason.CostUnknown, (int?)Aaa)]);
        test.Opening.ShouldBe(0.00m);
        test.Outcome.ShouldBe(ThresholdOutcome.Unknown);
    }

    [Test]
    public void NoThreshold_MakesTheTestIncomplete_AndNamesNoHolding_WhateverTheTotal()
    {
        var test = Test(2027, [NzdBuy(1, Utc(2026, 5, 1, 0, 0), 10m, 200_000.00m)], AaaAttributing);

        test.Amount.ShouldBeNull();
        Reasons(test).ShouldBe([(ThresholdIncompleteReason.NoThreshold, (int?)null)]);
        test.Peak.Total.ShouldBe(200_000.00m);
        test.Outcome.ShouldBe(ThresholdOutcome.Unknown);
    }

    [Test]
    public void AnIncompleteTest_WhoseKnownCostsAreAbove_IsAbove()
    {
        var facts = new[] { NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 60_000.00m), NzdBuy(2, Utc(2025, 5, 2, 0, 0), 10m, 100.00m, Bbb) };

        var test = Test(2026, facts, AaaAttributing);

        test.Complete.ShouldBeFalse();
        test.Outcome.ShouldBe(ThresholdOutcome.Above);
    }

    [Test]
    public void EveryReason_IsListed_InTheOrderOfTheTable()
    {
        var facts = new[]
        {
            UsdBuy(1, Utc(2026, 5, 20, 0, 0), 5m, 100.00m),
            NzdBuy(2, Utc(2026, 5, 2, 0, 0), 10m, 100.00m, Bbb)
        };

        Reasons(Test(2027, facts, AaaAttributing)).ShouldBe(
        [
            (ThresholdIncompleteReason.NotClassified, (int?)Bbb),
            (ThresholdIncompleteReason.CostUnknown, Aaa),
            (ThresholdIncompleteReason.NoThreshold, null)
        ]);
    }

    [Test]
    public void ATreatmentThatWasExemptInAnEarlierYearWhileHeld_IsTreatmentChanged_AndTheCostStillCounts()
    {
        var facts = new[] { NzdBuy(1, Utc(2022, 5, 1, 0, 0), 10m, 60_000.00m) };
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment>
        {
            [(Aaa, 2023)] = FifTreatment.ExemptAsxListed,
            [(Aaa, 2024)] = FifTreatment.Attributing,
            [(Aaa, 2025)] = FifTreatment.Attributing
        };

        var test = Test(2026, facts, AaaAttributing, earlierTreatments: earlier);

        Reasons(test).ShouldBe([(ThresholdIncompleteReason.TreatmentChanged, (int?)Aaa)]);
        test.Opening.ShouldBe(60_000.00m);
        test.Outcome.ShouldBe(ThresholdOutcome.Above);
    }

    [TestCase(FifTreatment.NotForeign)]
    [TestCase(FifTreatment.ExemptOther)]
    public void EveryTreatmentThatIsNotAnAttributingInterest_IsTreatmentChanged(FifTreatment treatment)
    {
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment> { [(Aaa, 2025)] = treatment };

        Reasons(Test(2026, [NzdBuy(1, Utc(2025, 1, 1, 0, 0), 10m, 100.00m)], AaaAttributing, earlierTreatments: earlier))
            .ShouldBe([(ThresholdIncompleteReason.TreatmentChanged, (int?)Aaa)]);
    }

    [Test]
    public void AnExemptYear_BeforeTheQuantityLastLeft0_IsNotTreatmentChanged()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2022, 5, 1, 0, 0), 10m, 1_000.00m),
            NzdSell(2, Utc(2024, 5, 1, 0, 0), 10m),
            NzdBuy(3, Utc(2024, 6, 1, 0, 0), 10m, 1_000.00m)
        };
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment>
        {
            [(Aaa, 2023)] = FifTreatment.ExemptOther,
            [(Aaa, 2024)] = FifTreatment.ExemptOther,
            [(Aaa, 2025)] = FifTreatment.Attributing
        };

        var test = Test(2026, facts, AaaAttributing, earlierTreatments: earlier);

        test.Complete.ShouldBeTrue();
        test.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
        // Without the sale the shares of 2022 are still held, and the exempt years count.
        Reasons(Test(2026, [facts[0], facts[2]], AaaAttributing, earlierTreatments: earlier))
            .ShouldBe([(ThresholdIncompleteReason.TreatmentChanged, (int?)Aaa)]);
    }

    [Test]
    public void ANotClassifiedEarlierYear_IsSkipped()
    {
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment> { [(Aaa, 2025)] = FifTreatment.Attributing };

        var test = Test(2026, [NzdBuy(1, Utc(2023, 5, 1, 0, 0), 10m, 1_000.00m)], AaaAttributing, earlierTreatments: earlier);

        test.Complete.ShouldBeTrue();
        test.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
        // The same holding with one exempt year among them is incomplete.
        var exempt = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment>(earlier) { [(Aaa, 2024)] = FifTreatment.ExemptOther };
        Reasons(Test(2026, [NzdBuy(1, Utc(2023, 5, 1, 0, 0), 10m, 1_000.00m)], AaaAttributing, earlierTreatments: exempt))
            .ShouldBe([(ThresholdIncompleteReason.TreatmentChanged, (int?)Aaa)]);
    }

    [Test]
    public void AHoldingBoughtInTheYear_HasNoEarlierYearToCheck()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2023, 5, 1, 0, 0), 10m, 1_000.00m),
            NzdSell(2, Utc(2024, 5, 1, 0, 0), 10m),
            NzdBuy(3, Utc(2025, 6, 1, 0, 0), 10m, 1_000.00m)
        };
        var earlier = new Dictionary<(int InstrumentId, int TaxYear), FifTreatment> { [(Aaa, 2024)] = FifTreatment.ExemptOther };

        var test = Test(2026, facts, AaaAttributing, earlierTreatments: earlier);

        test.Complete.ShouldBeTrue();
        test.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
        // Held at the opening instead, it is incomplete.
        Reasons(Test(2026, [facts[0]], AaaAttributing, earlierTreatments: earlier))
            .ShouldBe([(ThresholdIncompleteReason.TreatmentChanged, (int?)Aaa)]);
    }
}
