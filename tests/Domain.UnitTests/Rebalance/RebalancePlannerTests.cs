using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalance;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Rebalance;

public class RebalancePlannerTests
{
    [Test]
    public void Plan_SellsHoldingsOutsideTheTargetThenBuysTheModel()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 0m,
            residual: 0m,
            holdings:
            [
                Holding(1, "VTI", "USD", 10m, 25m, quoteToBase: 2m, quoteToAccount: 2m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 100m, 10m, quoteToBase: 1m, quoteToAccount: 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Select(trade => (trade.Symbol, trade.Buy, trade.Quantity)).ShouldBe([
            ("VTI", false, 10m),
            ("AIA", true, 50m)
        ]);
    }

    [Test]
    public void Plan_KeepsTheCashResidualOutOfTheBuy()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 0m,
            residual: 1m,
            holdings:
            [
                Holding(1, "VTI", "USD", 10m, 25m, quoteToBase: 2m, quoteToAccount: 2m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 100m, 10m, quoteToBase: 1m, quoteToAccount: 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Single(trade => trade.Symbol == "AIA").Quantity.ShouldBe(49.5m);
    }

    [Test]
    public void Plan_BuysATargetInsideAbsoluteDrift()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 199m,
            residual: 0m,
            holdings:
            [
                Holding(2, "AIA", "NZD", 1980.1m, 10m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Select(trade => (trade.Symbol, trade.Buy, trade.Quantity)).ShouldBe([
            ("AIA", true, 19.9m)
        ]);
    }

    [Test]
    public void Plan_SellsATargetInsideRelativeDrift()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 1800m,
            residual: 0m,
            holdings:
            [
                Holding(2, "AIA", "NZD", 820m, 10m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 80m, 10m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Select(trade => (trade.Symbol, trade.Buy, trade.Quantity)).ShouldBe([
            ("AIA", false, 20m)
        ]);
    }

    [Test]
    public void Plan_SellsOverweightTargetsBeforeBuyingUnderweightTargets()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 0m,
            residual: 0m,
            holdings:
            [
                Holding(1, "AAA", "NZD", 60m, 100m, 1m, 1m),
                Holding(2, "BBB", "NZD", 40m, 100m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "BBB", "NZD", 60m, 100m, 1m, 1m),
                Target(1, "AAA", "NZD", 40m, 100m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Select(trade => (trade.Symbol, trade.Buy, trade.Quantity)).ShouldBe([
            ("AAA", false, 20m),
            ("BBB", true, 20m)
        ]);
    }

    [Test]
    public void Plan_SkipsATargetTradeBelowTheMinimum()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 50m,
            residual: 0m,
            minimumTrade: 100m,
            holdings:
            [
                Holding(2, "AIA", "NZD", 95m, 10m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.NoChange);
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_FailsWhenAPriceIsMissing()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 1000m,
            residual: 0m,
            holdings: [],
            targets: [Target(2, "AIA", "NZD", 100m, null, 1m, 1m)]));

        plan.Status.ShouldBe(RebalanceStatus.Failed);
        plan.Code.ShouldBe("missing_price");
        plan.Message.ShouldNotBeNull().ShouldContain("AIA");
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_FailsWhenFxIsMissing()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 0m,
            residual: 0m,
            holdings: [Holding(1, "VTI", "USD", 10m, 25m, null, 2m)],
            targets: [Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)]));

        plan.Status.ShouldBe(RebalanceStatus.Failed);
        plan.Code.ShouldBe("missing_fx");
        plan.Message.ShouldNotBeNull().ShouldContain("USD");
        plan.Message.ShouldContain("NZD");
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_ScalesTheBuysWhenCashCannotFundThem()
    {
        // Trade fees R27 (amends portfolios R11): a shortfall scales the buys instead of failing.
        var plan = RebalancePlanner.Plan(new RebalanceInput(
            "NZD",
            "NZD",
            100m,
            1m,
            0m,
            0m,
            [],
            [Target(2, "AIA", "USD", 100m, 10m, 1m, 2m)]));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Select(trade => (trade.Symbol, trade.Buy, trade.Quantity)).ShouldBe([("AIA", true, 5m)]);
    }

    [Test]
    public void Plan_WithFees_TheResidualAbsorbsTheFeeWithoutScaling()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 10000m,
            residual: 1m,
            holdings: [],
            targets: [Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)],
            fees: Fees(minimum: 10m)));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Single().Quantity.ShouldBe(990m);
    }

    [Test]
    public void Plan_WithFees_ScalesEveryBuyByOneFactorAndLeavesCashJustAboveZero()
    {
        var schedule = Schedule(minimum: 10m);
        var plan = RebalancePlanner.Plan(Input(
            cash: 10000m,
            residual: 0m,
            holdings: [],
            targets:
            [
                Target(2, "AIA", "NZD", 50m, 10m, 1m, 1m),
                Target(3, "FPH", "NZD", 50m, 10m, 1m, 1m)
            ],
            fees: Fees(schedule)));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.ShouldAllBe(trade => trade.Buy);
        var buys = plan.Trades;
        buys.Count.ShouldBe(2);
        buys[0].Quantity.ShouldBe(buys[1].Quantity);
        buys[0].Quantity.ShouldBeLessThan(500m);
        var cash = 10000m - buys.Sum(trade =>
            decimal.Round(trade.Quantity * trade.Price, 4, MidpointRounding.AwayFromZero)
            + schedule.FeeFor(decimal.Round(trade.Quantity * trade.Price, 4, MidpointRounding.AwayFromZero), 2).Fee);
        cash.ShouldBeGreaterThanOrEqualTo(0m);
        cash.ShouldBeLessThan(0.5m);
    }

    [Test]
    public void Plan_WithFees_DropsAScaledBuyBelowTheMinimumTrade()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 10000m,
            residual: 0m,
            holdings: [],
            targets:
            [
                Target(2, "AIA", "NZD", 98m, 10m, 1m, 1m),
                Target(3, "FPH", "NZD", 2m, 10m, 1m, 1m)
            ],
            minimumTrade: 199m,
            fees: Fees(minimum: 50m)));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        var buy = plan.Trades.ShouldHaveSingleItem();
        buy.Symbol.ShouldBe("AIA");
        buy.Quantity.ShouldBeLessThan(980m);
        buy.Quantity.ShouldBeGreaterThan(970m);
    }

    [Test]
    public void Plan_WithFees_NoBuyLeftAfterScaling_IsNoChange()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 100m,
            residual: 0m,
            holdings: [],
            targets: [Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)],
            minimumTrade: 1m,
            fees: Fees(minimum: 100m)));

        plan.Status.ShouldBe(RebalanceStatus.NoChange);
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_SellFeesAboveCashAndSellProceeds_FailsWithInsufficientCash()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 0m,
            residual: 0m,
            holdings: [Holding(1, "VTI", "NZD", 1m, 1m, 1m, 1m)],
            targets: [Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)],
            minimumTrade: 0m,
            fees: Fees(minimum: 10m)));

        plan.Status.ShouldBe(RebalanceStatus.Failed);
        plan.Code.ShouldBe("insufficient_cash");
        plan.Message.ShouldNotBeNull().ShouldContain("NZD");
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_WithFees_OnlyTheTradesQuoteCurrencyScheduleApplies()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 10000m,
            residual: 0m,
            holdings: [],
            targets: [Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)],
            fees: new Dictionary<string, PlannerFeeSchedule>
            {
                ["USD"] = new(Schedule(minimum: 500m, currency: "USD"), 2)
            }));

        plan.Status.ShouldBe(RebalanceStatus.Success);
        plan.Trades.Single().Quantity.ShouldBe(1000m);
    }

    private static TradeFeeSchedule Schedule(decimal minimum, string currency = "NZD")
    {
        var schedule = TradeFeeSchedule.Create(
            1, Currency.Create(currency, currency, 2), new DateOnly(2026, 12, 1), minimum, null,
            [new FeeBand(0m, 0.5m)], new DateOnly(2026, 11, 15));
        schedule.Id = 9;
        return schedule;
    }

    private static IReadOnlyDictionary<string, PlannerFeeSchedule> Fees(decimal minimum) => Fees(Schedule(minimum));

    private static IReadOnlyDictionary<string, PlannerFeeSchedule> Fees(TradeFeeSchedule schedule) =>
        new Dictionary<string, PlannerFeeSchedule> { [schedule.QuoteCurrency] = new(schedule, 2) };

    private static RebalanceInput Input(
        decimal cash,
        decimal residual,
        PlannerHolding[] holdings,
        PlannerTarget[] targets,
        decimal minimumTrade = 100m,
        IReadOnlyDictionary<string, PlannerFeeSchedule>? fees = null) =>
        new(
            "NZD",
            "NZD",
            cash,
            1m,
            minimumTrade,
            residual,
            holdings,
            targets,
            fees);

    private static PlannerHolding Holding(
        int id,
        string symbol,
        string quote,
        decimal quantity,
        decimal? price,
        decimal? quoteToBase,
        decimal? quoteToAccount) =>
        new(id, symbol, quote, quantity, price, quoteToBase, quoteToAccount);

    private static PlannerTarget Target(
        int id,
        string symbol,
        string quote,
        decimal weight,
        decimal? price,
        decimal? quoteToBase,
        decimal? quoteToAccount) =>
        new(id, symbol, quote, weight, price, quoteToBase, quoteToAccount);
}
