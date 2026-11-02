using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalance;
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
    public void Plan_SkipsALineInsideAbsoluteDrift()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 8m,
            residual: 0m,
            holdings:
            [
                Holding(2, "AIA", "NZD", 99.2m, 10m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 100m, 10m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.NoChange);
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_SkipsALineInsideRelativeDrift()
    {
        var plan = RebalancePlanner.Plan(Input(
            cash: 215m,
            residual: 0m,
            holdings:
            [
                Holding(2, "AIA", "NZD", 78.5m, 10m, 1m, 1m)
            ],
            targets:
            [
                Target(2, "AIA", "NZD", 80m, 10m, 1m, 1m)
            ]));

        plan.Status.ShouldBe(RebalanceStatus.NoChange);
        plan.Trades.ShouldBeEmpty();
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

    private static RebalanceInput Input(
        decimal cash,
        decimal residual,
        PlannerHolding[] holdings,
        PlannerTarget[] targets,
        decimal minimumTrade = 100m) =>
        new(
            "NZD",
            "NZD",
            cash,
            1m,
            DriftAbsolute: 1m,
            DriftRelative: 5m,
            minimumTrade,
            residual,
            holdings,
            targets);

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
