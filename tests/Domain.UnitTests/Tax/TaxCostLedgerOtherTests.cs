using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold R9–R14: arrivals, departures, corrections, an unknown cost and the rates the walk reads.</summary>
public class TaxCostLedgerOtherTests
{
    private static readonly DateOnly May20 = new(2025, 5, 20);

    private static TaxCostWalk Walk(IReadOnlyList<LedgerFact> facts, params (string Currency, DateOnly Date, StoredRate Row)[] rates) =>
        TaxCostLedger.Walk(
            TaxLedger.Build(facts), new HashSet<int> { Aaa }, rates.ToDictionary(rate => (rate.Currency, rate.Date), rate => rate.Row), 2);

    private static (string, DateOnly, StoredRate) Usd(DateOnly date, decimal rate, DateOnly? asOf = null) =>
        ("USD", date, new StoredRate(rate, asOf ?? date, "Frankfurter"));

    private static LedgerSecurityLeg Leg(int legId, int accountId, decimal quantity, decimal cost, string currency) =>
        new(legId, accountId, ModeOf(accountId), Aaa, quantity, cost, currency);

    private static LedgerFact NzdBuy(int id, DateTimeOffset at, decimal quantity, decimal principal) =>
        Fact(id, at, TransactionType.Buy, [Cash(A2, CashLegRole.Principal, -principal)], [Security(id, A2, quantity, 0m)]);

    private static LedgerFact NzdSell(int id, DateTimeOffset at, decimal quantity) =>
        Fact(id, at, TransactionType.Sell, [Cash(A2, CashLegRole.Principal, 1m)], [Security(id, A2, -quantity, 0m)]);

    [Test]
    public void AnArrival_WithAcquiredOn_TakesTheRateOfThatDate()
    {
        var stockIn = Fact(1, Utc(2025, 8, 4, 3, 0), TransactionType.StockIn, [], [Security(1, A1, 60m, 2_400.00m)],
            inKind: new LedgerInKindTransfer(false, new DateOnly(2024, 6, 14)));

        var walk = Walk([stockIn], Usd(new DateOnly(2024, 6, 14), 1.6300m), Usd(new DateOnly(2025, 8, 4), 1.9000m));

        var step = walk.Steps.ShouldHaveSingleItem();
        step.Change.ShouldBe(3_912.00m);
        step.CostAfter.ShouldBe(3_912.00m);
        step.Fx.ShouldBe(new StoredRate(1.6300m, new DateOnly(2024, 6, 14), "Frankfurter"));
    }

    [Test]
    public void AnArrival_WithoutAcquiredOn_TakesTheRateOfTheUtcDateOfBookedAt()
    {
        var opening = Fact(1, Utc(2025, 5, 20, 22, 0), TransactionType.OpeningHoldings, [], [Security(1, A1, 10m, 500.00m)]);

        var walk = Walk([opening], Usd(May20, 1.6900m));

        walk.Steps.Select(step => (step.Change, step.Fx?.Rate)).ShouldBe([((decimal?)845.00m, (decimal?)1.6900m)]);
    }

    [Test]
    public void AnArrival_QuotedInNzd_IsUsedAsItIs()
    {
        var opening = Fact(1, Utc(2025, 5, 20, 22, 0), TransactionType.OpeningHoldings, [], [Leg(1, A2, 10m, 1_234.5678m, "NZD")]);

        var walk = Walk([opening]);

        walk.Steps.Select(step => (step.Change, step.Fx)).ShouldBe([((decimal?)1_234.57m, (StoredRate?)null)]);
    }

    [Test]
    public void AnArrival_WithACarriedCostOf0_IsNoCarriedCost()
    {
        var opening = Fact(1, Utc(2025, 5, 20, 22, 0), TransactionType.OpeningHoldings, [], [Security(1, A1, 10m, 0m)]);

        var walk = Walk([opening], Usd(May20, 1.6900m));

        walk.Steps.Select(step => (step.QuantityAfter, step.Change, step.CostAfter, step.Unknown)).ShouldBe(
        [
            (10m, (decimal?)null, (decimal?)null,
                (TaxCostUnknown?)new TaxCostUnknown(TaxCostUnknownCause.NoCarriedCost, 0, Utc(2025, 5, 20, 22, 0), IdOf(1)))
        ]);
    }

    [Test]
    public void ADeparture_RemovesTheAverage()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 1_000.00m),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.StockOut, [], [Security(2, A2, -4m, -228.00m)])
        };

        Walk(facts).Steps.Select(step => (step.Event.Kind, step.Change, step.CostAfter)).ShouldBe(
        [
            (TaxEventKind.Acquisition, (decimal?)1_000.00m, (decimal?)1_000.00m),
            (TaxEventKind.Departure, -400.00m, 600.00m)
        ]);
    }

    [Test]
    public void ACorrection_AppliesBothDeltasAsStated_ReleasesNoAverage_AndNeedsNoRateForACostChangeOf0()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 1_000.00m),
            Fact(2, Utc(2025, 5, 20, 1, 0), TransactionType.Adjustment, [], [Security(2, A1, 2m, 150.00m)]),
            Fact(3, Utc(2025, 5, 21, 1, 0), TransactionType.Adjustment, [], [Leg(3, A2, -7m, -100.00m, "NZD")]),
            Fact(4, Utc(2025, 6, 1, 1, 0), TransactionType.Adjustment, [], [Security(4, A1, 1m, 0m)])
        };

        var walk = Walk(facts, Usd(May20, 1.6900m));

        walk.Steps.Select(step => (step.QuantityAfter, step.Change, step.CostAfter, step.Fx?.Rate)).ShouldBe(
        [
            (10m, (decimal?)1_000.00m, (decimal?)1_000.00m, (decimal?)null),
            (12m, 253.50m, 1_253.50m, 1.6900m),
            (5m, -100.00m, 1_153.50m, null),
            // A quantity-only correction needs no rate: there is no figure to convert.
            (6m, 0.00m, 1_153.50m, null)
        ]);
    }

    [Test]
    public void ACorrection_ThatLeavesACostBelow0_IsNegativeCost()
    {
        var facts = new[]
        {
            NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 100.00m),
            Fact(2, Utc(2025, 5, 2, 0, 0), TransactionType.Adjustment, [], [Leg(2, A2, 0m, -200.00m, "NZD")])
        };

        Walk(facts).Steps.Select(step => (step.CostAfter, step.Unknown?.Cause)).ShouldBe(
        [
            ((decimal?)100.00m, (TaxCostUnknownCause?)null),
            (null, TaxCostUnknownCause.NegativeCost)
        ]);
    }

    [Test]
    public void AnAcquisition_WithNoRateOrAStaleRate_IsUnknown()
    {
        var buy = Fact(1, Utc(2025, 5, 20, 2, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -100.00m)], [Security(1, A1, 1m, 100.00m)]);

        Walk([buy]).Steps.Select(step => (step.CostAfter, step.Unknown?.Cause)).ShouldBe([((decimal?)null, (TaxCostUnknownCause?)TaxCostUnknownCause.NoRate)]);
        Walk([buy], Usd(May20, 1.6900m, new DateOnly(2025, 5, 10))).Steps.Select(step => (step.CostAfter, step.Unknown?.Cause))
            .ShouldBe([((decimal?)null, (TaxCostUnknownCause?)TaxCostUnknownCause.StaleRate)]);
    }

    [Test]
    public void AnArrival_WithNoRateOrAStaleRate_IsUnknown()
    {
        var opening = Fact(1, Utc(2025, 5, 20, 22, 0), TransactionType.OpeningHoldings, [], [Security(1, A1, 10m, 500.00m)]);

        Walk([opening]).Steps.Select(step => step.Unknown?.Cause).ShouldBe([TaxCostUnknownCause.NoRate]);
        Walk([opening], Usd(May20, 1.6900m, new DateOnly(2025, 5, 1))).Steps.Select(step => step.Unknown?.Cause)
            .ShouldBe([TaxCostUnknownCause.StaleRate]);
    }

    [Test]
    public void ADisposalOfMoreThanIsHeld_IsNegativeQuantity()
    {
        var facts = new[] { NzdBuy(1, Utc(2025, 5, 1, 0, 0), 10m, 1_000.00m), NzdSell(2, Utc(2025, 5, 2, 0, 0), 12m) };

        Walk(facts).Steps.Select(step => (step.QuantityAfter, step.CostAfter, step.Unknown?.Cause)).ShouldBe(
        [
            (10m, (decimal?)1_000.00m, (TaxCostUnknownCause?)null),
            (-2m, null, TaxCostUnknownCause.NegativeQuantity)
        ]);
    }

    [Test]
    public void AnUnknownCost_StaysUnknownWithItsFirstCause_UntilNothingIsHeld_ThenIs000AndKnown()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 20, 2, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -100.00m)], [Security(1, A1, 10m, 100.00m)]),
            NzdBuy(2, Utc(2025, 5, 21, 0, 0), 5m, 500.00m),
            NzdSell(3, Utc(2025, 5, 22, 0, 0), 20m),
            NzdBuy(4, Utc(2025, 5, 23, 0, 0), 5m, 600.00m),
            NzdSell(5, Utc(2025, 5, 24, 0, 0), 10m),
            NzdBuy(6, Utc(2025, 5, 25, 0, 0), 2m, 200.00m)
        };

        var walk = Walk(facts);

        var first = new TaxCostUnknown(TaxCostUnknownCause.NoRate, 0, Utc(2025, 5, 20, 2, 0), IdOf(1));
        walk.Steps.Select(step => (step.QuantityAfter, step.Change, step.CostAfter, step.Unknown)).ShouldBe(
        [
            (10m, (decimal?)null, (decimal?)null, (TaxCostUnknown?)first),
            (15m, null, null, first),
            (-5m, null, null, first),
            (0m, null, 0.00m, null),
            (-10m, null, null, new TaxCostUnknown(TaxCostUnknownCause.NegativeQuantity, 4, Utc(2025, 5, 24, 0, 0), IdOf(5))),
            (-8m, null, null, new TaxCostUnknown(TaxCostUnknownCause.NegativeQuantity, 4, Utc(2025, 5, 24, 0, 0), IdOf(5)))
        ]);
        walk.At(Aaa, Utc(2025, 5, 23, 12, 0)).ShouldBe(new TaxCostState(0m, 0.00m, null));
    }

    [Test]
    public void RateKeys_AreTheDatesOfEachFigureInAnotherCurrency_OfTheCostedInstruments()
    {
        var facts = new[]
        {
            Fact(1, Utc(2025, 5, 12, 20, 0), TransactionType.Buy,
                [Cash(A1, CashLegRole.Principal, -25_000.00m), Cash(A1, CashLegRole.Fee, -10.00m)], [Security(1, A1, 500m, 25_010.00m)]),
            Fact(2, Utc(2025, 8, 4, 3, 0), TransactionType.StockIn, [], [Security(2, A1, 60m, 2_400.00m)],
                inKind: new LedgerInKindTransfer(false, new DateOnly(2024, 6, 14))),
            Fact(3, Utc(2025, 9, 1, 23, 0), TransactionType.OpeningHoldings, [], [Security(3, A1, 5m, 50.00m)]),
            Fact(4, Utc(2025, 9, 2, 1, 0), TransactionType.Adjustment, [], [Security(4, A1, 1m, 10.00m)]),
            Fact(5, Utc(2025, 9, 3, 1, 0), TransactionType.Adjustment, [], [Security(5, A1, 1m, 0m)]),
            Fact(6, Utc(2025, 9, 4, 1, 0), TransactionType.OpeningHoldings, [], [Security(6, A1, 5m, 0m)]),
            Fact(7, Utc(2025, 9, 5, 1, 0), TransactionType.OpeningHoldings, [], [Leg(7, A2, 5m, 50.00m, "NZD")]),
            NzdBuy(8, Utc(2025, 9, 6, 1, 0), 1m, 10.00m),
            Fact(9, Utc(2025, 9, 7, 1, 0), TransactionType.Buy, [Cash(A1, CashLegRole.Principal, -10.00m)], [Security(9, A1, 1m, 10.00m, 11)])
        };

        TaxCostLedger.RateKeys(TaxLedger.Build(facts), new HashSet<int> { Aaa }).OrderBy(key => key.Date).ShouldBe(
        [
            ("USD", new DateOnly(2024, 6, 14)),
            ("USD", new DateOnly(2025, 5, 12)),
            ("USD", new DateOnly(2025, 9, 1)),
            ("USD", new DateOnly(2025, 9, 2))
        ]);
    }
}
