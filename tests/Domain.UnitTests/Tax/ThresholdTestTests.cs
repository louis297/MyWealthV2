using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.ThresholdExamples;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold R16–R19, R24: which tax holdings count, the totals, the peak, the outcome and the steps.</summary>
public class ThresholdTestTests
{
    private static LedgerFact NzdBuy(int id, DateTimeOffset at, decimal quantity, decimal principal, int instrumentId = Aaa) =>
        Fact(id, at, TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -principal)], [Security(id, A2, quantity, 0m, instrumentId)]);

    private static LedgerFact NzdSell(int id, DateTimeOffset at, decimal quantity, int instrumentId = Aaa) =>
        Fact(id, at, TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 1m)], [Security(id, A2, -quantity, 0m, instrumentId)]);

    private static IReadOnlyDictionary<int, TaxTreatment> AaaAttributing => new Dictionary<int, TaxTreatment> { [Aaa] = Attributing };

    [TestCase(2022, 0.00, 55_000.00, "2022-03-30T22:00:00Z", 55_000.00, ThresholdOutcome.Above)]
    [TestCase(2023, 55_000.00, 55_000.00, "2022-03-31T11:00:00Z", 55_000.00, ThresholdOutcome.Above)]
    [TestCase(2024, 55_000.00, 55_000.00, "2023-03-31T11:00:00Z", 27_500.00, ThresholdOutcome.Above)]
    [TestCase(2025, 27_500.00, 27_500.00, "2024-03-31T11:00:00Z", 27_500.00, ThresholdOutcome.NotAbove)]
    public void Example2_Jax_OverFourTaxYears(int taxYear, decimal opening, decimal peak, string at, decimal closing, ThresholdOutcome outcome)
    {
        var test = Test(taxYear, Jax, JaxTreatments);

        test.Amount.ShouldBe(50_000.00m);
        test.Opening.ShouldBe(opening);
        test.Peak.Total.ShouldBe(peak);
        test.Peak.At.ShouldBe(DateTimeOffset.Parse(at));
        test.Closing.ShouldBe(closing);
        test.Outcome.ShouldBe(outcome);
        test.Complete.ShouldBeTrue();
    }

    [Test]
    public void Example2_ThePeakOf2022_IsTheSecondBuy_AndThePeakOfALaterYear_IsTheOpening()
    {
        var first = Test(2022, Jax, JaxTreatments);
        first.Peak.InstrumentId.ShouldBe(Bbb);
        first.Peak.Event!.TransactionId.ShouldBe(2);

        var later = Test(2024, Jax, JaxTreatments);
        later.Peak.InstrumentId.ShouldBeNull();
        later.Peak.Event.ShouldBeNull();
        later.Steps.Select(step => (step.Event.TransactionId, step.EventSequence, step.Change, step.Total)).ShouldBe(
        [
            (3, 1, (decimal?)-15_000.00m, 40_000.00m),
            (4, 1, -12_500.00m, 27_500.00m)
        ]);
    }

    [Test]
    public void Example3_ThePeakIsReachedAtU4_ByTheRateOfThatDay()
    {
        var test = Test(2026, Crossing, CrossingTreatments, CrossingRates());

        test.Opening.ShouldBe(0.00m);
        test.Peak.Total.ShouldBe(50_046.40m);
        test.Peak.At.ShouldBe(Utc(2025, 10, 9, 19, 30));
        test.Peak.InstrumentId.ShouldBe(Bbb);
        test.Peak.Event!.TransactionId.ShouldBe(4);
        test.Closing.ShouldBe(26_979.20m);
        test.Outcome.ShouldBe(ThresholdOutcome.Above);
        test.Complete.ShouldBeTrue();
        test.Steps.Select(step => (step.Event.TransactionId, step.Event.InstrumentId, step.EventSequence, step.Event.Kind, step.Change, step.Total))
            .ShouldBe(
            [
                (2, Bbb, 1, TaxEventKind.Acquisition, (decimal?)42_266.90m, 42_266.90m),
                (3, Ddd, 1, TaxEventKind.Arrival, 3_912.00m, 46_178.90m),
                (4, Bbb, 2, TaxEventKind.Acquisition, 3_867.50m, 50_046.40m),
                (5, Bbb, 3, TaxEventKind.Disposal, -23_067.20m, 26_979.20m)
            ]);
    }

    [Test]
    public void Example3_AtARateOf17000_IsNotAbove()
    {
        var test = Test(2026, Crossing, CrossingTreatments, CrossingRates(october9: 1.7000m));

        test.Peak.Total.ShouldBe(49_935.90m);
        test.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
    }

    [TestCase(50_000.00, ThresholdOutcome.NotAbove)]
    [TestCase(50_000.01, ThresholdOutcome.Above)]
    public void AboveMeansMoreThanTheThreshold(decimal paid, ThresholdOutcome outcome)
    {
        Test(2026, [NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, paid)], AaaAttributing).Outcome.ShouldBe(outcome);
    }

    [Test]
    public void AnExemptAndANewZealandHolding_AreNotCounted_AndNeedNoRate()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 1, 0, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -90_000.00m)], [Security(1, A1, 10m, 0m, Ccc)]),
            NzdBuy(2, Utc(2025, 5, 2, 0, 0), 10m, 90_000.00m, Ddd),
            NzdBuy(3, Utc(2025, 5, 3, 0, 0), 10m, 1_000.00m)
        };
        var treatments = new Dictionary<int, TaxTreatment>
        {
            [Aaa] = Attributing,
            [Ccc] = new(FifTreatment.ExemptOther, 2009, "A unit trust", "AU"),
            [Ddd] = new(FifTreatment.NotForeign, 2009, null, "NZ")
        };

        var test = Test(2026, facts, treatments);

        test.Peak.Total.ShouldBe(1_000.00m);
        test.Outcome.ShouldBe(ThresholdOutcome.NotAbove);
        test.Complete.ShouldBeTrue();
    }

    [Test]
    public void ABuyAndASaleOnOneDay_ShowInThePeak()
    {
        var facts = new[] { NzdBuy(1, Utc(2025, 5, 1, 1, 0), 10m, 60_000.00m), NzdSell(2, Utc(2025, 5, 1, 4, 0), 10m) };

        var test = Test(2026, facts, AaaAttributing);

        test.Peak.Total.ShouldBe(60_000.00m);
        test.Closing.ShouldBe(0.00m);
        test.Outcome.ShouldBe(ThresholdOutcome.Above);
    }

    [Test]
    public void ThePeak_IsTheFirstPointOfATie()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 30_000.00m),
            NzdSell(2, Utc(2025, 6, 1, 0, 0), 5m),
            NzdBuy(3, Utc(2025, 7, 1, 0, 0), 5m, 15_000.00m)
        };

        var test = Test(2026, facts, AaaAttributing);

        test.Peak.Total.ShouldBe(30_000.00m);
        test.Peak.Event!.TransactionId.ShouldBe(1);
    }

    [Test]
    public void ACustomerWithNothing_Is000_NotAbove_AndComplete()
    {
        var test = Test(2026, [], new Dictionary<int, TaxTreatment>());

        test.ShouldBe(test with { Opening = 0.00m, Closing = 0.00m, Outcome = ThresholdOutcome.NotAbove });
        test.Peak.ShouldBe(new ThresholdPeak(0.00m, TaxYear.Start(2026), null, null));
        test.Complete.ShouldBeTrue();
        test.Steps.ShouldBeEmpty();
    }

    [Test]
    public void TheSteps_AddUpToTheTotals()
    {
        foreach (var test in new[] { Test(2026, Crossing, CrossingTreatments, CrossingRates()), Test(2024, Jax, JaxTreatments) })
        {
            test.Steps.ShouldNotBeEmpty();
            var total = test.Opening;
            foreach (var step in test.Steps)
            {
                total += step.Change!.Value;
                step.Total.ShouldBe(total);
            }

            total.ShouldBe(test.Closing);
            test.Peak.Total.ShouldBeGreaterThanOrEqualTo(Math.Max(test.Opening, test.Closing));
        }
    }
}
