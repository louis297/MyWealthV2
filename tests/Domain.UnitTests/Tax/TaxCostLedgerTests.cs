using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold R5–R8, R12 for trades: example 1 of §1.1 and the release of the average.</summary>
public class TaxCostLedgerTests
{
    internal static TaxCostWalk Walk(IReadOnlyList<LedgerFact> facts, IReadOnlyList<StoredRate>? rates = null, params int[] costed)
    {
        var events = TaxLedger.Build(facts);
        return TaxCostLedger.Walk(
            events, (costed.Length == 0 ? [Aaa] : costed).ToHashSet(), RatesFor(events, rates ?? UsdRates), 2);
    }

    [Test]
    public void Example1_TheTaxCostAfterEachEvent_ToTheCent()
    {
        var walk = Walk(Facts);

        walk.Steps.Select(step => step.Event.Kind).ShouldBe(
        [
            TaxEventKind.Acquisition, TaxEventKind.Acquisition, TaxEventKind.Disposal, TaxEventKind.Dividend,
            TaxEventKind.Reorganisation, TaxEventKind.Reorganisation, TaxEventKind.Dividend, TaxEventKind.Acquisition,
            TaxEventKind.Move, TaxEventKind.Acquisition
        ]);
        walk.Steps.Select(step => step.QuantityAfter).ShouldBe([100m, 140m, 110m, 110m, 150m, 220m, 220m, 220.8m, 220.8m, 240.8m]);
        walk.Steps.Select(step => step.Change).ShouldBe(
            new decimal?[] { 8_708.70m, 3_647.00m, -2_647.65m, 0m, 0m, 0m, 0m, 37.63m, 0m, 1_046.65m });
        walk.Steps.Select(step => step.CostAfter).ShouldBe(
            new decimal?[] { 8_708.70m, 12_355.70m, 9_708.05m, 9_708.05m, 9_708.05m, 9_708.05m, 9_708.05m, 9_745.68m, 9_745.68m, 10_792.33m });
        walk.Steps.ShouldAllBe(step => step.Unknown == null && step.Fx == null);
    }

    [Test]
    public void Example1_TheTaxCostAtTheOpeningAndTheClosingOfEachYear()
    {
        var walk = Walk(Facts);

        walk.At(Aaa, TaxYear.Start(2025)).ShouldBe(new TaxCostState(0m, 0m, null));
        walk.At(Aaa, TaxYear.End(2025)).ShouldBe(new TaxCostState(140m, 12_355.70m, null));
        walk.At(Aaa, TaxYear.Start(2026)).ShouldBe(walk.At(Aaa, TaxYear.End(2025)));
        walk.At(Aaa, TaxYear.End(2026)).ShouldBe(new TaxCostState(240.8m, 10_792.33m, null));
    }

    [Test]
    public void AReversedPurchase_IsNotInTheWalk()
    {
        var walk = Walk(Facts);

        walk.Steps.ShouldNotContain(step => step.Event.TransactionId == 5 || step.Event.TransactionId == 6);
        walk.Steps.Max(step => step.CostAfter).ShouldBe(12_355.70m);
    }

    [Test]
    public void AnAcquisition_FromAUsdAccount_ConvertsThePrincipalAndTheFeeEachOnItsOwn()
    {
        var buy = Fact(1, Utc(2025, 5, 20, 2, 0), TransactionType.Buy,
            [Cash(A1, CashLegRole.Principal, -10.01m), Cash(A1, CashLegRole.Fee, -0.01m)], [Security(1, A1, 1m, 10.02m)]);

        var walk = Walk([buy], [Rate(2025, 5, 20, 1.5000m)]);

        // 10.01 × 1.5 = 15.015 → 15.02 and 0.01 × 1.5 = 0.015 → 0.02; not 10.02 × 1.5 = 15.03.
        walk.Steps.Select(step => step.Change).ShouldBe(new decimal?[] { 15.04m });
    }

    [Test]
    public void AnAcquisition_FromAnNzdAccount_IsUsedAsBooked_WithNoRate()
    {
        var buy = Fact(1, Utc(2025, 5, 20, 2, 0), TransactionType.Buy,
            [Cash(A2, CashLegRole.Principal, -3_640.00m), Cash(A2, CashLegRole.Fee, -7.00m)], [Security(1, A2, 40m, 2_084.00m)]);

        var walk = Walk([buy], []);

        walk.Steps.Select(step => step.Change).ShouldBe(new decimal?[] { 3_647.00m });
        walk.Steps.ShouldAllBe(step => step.Unknown == null);
    }

    [Test]
    public void ADisposal_RemovesTheAverage_RoundedAwayFromZero_AndTheLastOneRemovesWhatIsLeft()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(1, A2, 3m, 57.00m)]),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 40.00m)], [Security(2, A2, -1m, -19.00m)]),
            Fact(3, Utc(2025, 5, 3, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 40.00m)], [Security(3, A2, -1m, -19.00m)]),
            Fact(4, Utc(2025, 5, 4, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 40.00m)], [Security(4, A2, -1m, -19.00m)])
        };

        var walk = Walk(facts, []);

        // 100.00 / 3 = 33.333… → 33.33; 66.67 / 2 = 33.335 → 33.34; the last share takes the 33.33 left.
        walk.Steps.Select(step => step.Change).ShouldBe(new decimal?[] { 100.00m, -33.33m, -33.34m, -33.33m });
        walk.Steps.Select(step => step.CostAfter).ShouldBe(new decimal?[] { 100.00m, 66.67m, 33.33m, 0.00m });
    }

    [Test]
    public void TheProceedsAndTheFeeOfASale_ChangeNothing()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -1_000.00m)], [Security(1, A2, 10m, 570.00m)]),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.Sell,
                [Cash(A2, CashLegRole.Principal, 9_999.00m), Cash(A2, CashLegRole.Fee, -50.00m)], [Security(2, A2, -5m, -285.00m)])
        };

        Walk(facts, []).Steps.Select(step => step.Change).ShouldBe(new decimal?[] { 1_000.00m, -500.00m });
    }

    [Test]
    public void NothingHeld_CostsNothing_AndANewPurchaseStartsAgain()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -1_000.00m)], [Security(1, A2, 10m, 570.00m)]),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 1_200.00m)], [Security(2, A2, -10m, -570.00m)]),
            Fact(3, Utc(2025, 5, 3, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -300.00m)], [Security(3, A2, 2m, 171.00m)])
        };

        var walk = Walk(facts, []);

        walk.Steps.Select(step => step.CostAfter).ShouldBe(new decimal?[] { 1_000.00m, 0.00m, 300.00m });
        walk.At(Aaa, Utc(2025, 5, 2, 12, 0)).ShouldBe(new TaxCostState(0m, 0.00m, null));
    }

    [Test]
    public void TheCost_IsOneAverageOverTwoAccounts()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 20, 1, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -1_000.00m)], [Security(1, A1, 100m, 1_000.00m)]),
            Fact(2, Utc(2025, 5, 20, 2, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -3_000.00m)], [Security(2, A2, 100m, 1_700.00m)]),
            Fact(3, Utc(2025, 5, 20, 3, 0), TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 2_000.00m)], [Security(3, A2, -50m, -850.00m)])
        };

        var walk = Walk(facts, [Rate(2025, 5, 20, 1.6900m)]);

        // 1,690.00 + 3,000.00 = 4,690.00 over 200 shares; the sale of 50 on A2 removes a quarter of it.
        walk.Steps.Select(step => step.CostAfter).ShouldBe(new decimal?[] { 1_690.00m, 4_690.00m, 3_517.50m });
    }

    [Test]
    public void OnlyTheCostedInstruments_HaveSteps()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 1, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -100.00m)], [Security(1, A2, 1m, 57.00m)]),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -200.00m)], [Security(2, A2, 1m, 114.00m, 11)])
        };

        var walk = Walk(facts, []);

        walk.Steps.Select(step => (step.Event.InstrumentId, step.CostAfter)).ShouldBe([(Aaa, (decimal?)100.00m)]);
        walk.At(11, TaxYear.End(2026)).ShouldBe(new TaxCostState(0m, 0m, null));
    }
}
