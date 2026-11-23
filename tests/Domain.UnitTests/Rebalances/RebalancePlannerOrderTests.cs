using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Orders;
using MyWealthV2.Domain.Rebalances;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Rebalances;

/// <summary>Rebalances R7, R8, R16 (docs/features/rebalances.md): the checks orders add to the plan, and buys sized by
/// the cash their orders reserve.</summary>
public class RebalancePlannerOrderTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    [Test]
    public void Plan_AgainstGivenTargets_WithoutTheOrderChecks_IsTodaysPlan()
    {
        var targets = new[] { Target(2, "ACME.SIM", "USD", 60m, 100m), Target(3, "BETA.SIM", "USD", 40m, 50m) };
        var ledger = RebalancePlanner.Plan(Input(10000m, targets));
        var orders = RebalancePlanner.Plan(Input(10000m, targets, orderChecks: true));

        ledger.Status.ShouldBe(RebalancePlanOutcome.Success);
        orders.Trades.ShouldBe(ledger.Trades);
        ledger.Trades.Select(trade => (trade.Symbol, trade.Quantity)).ShouldBe([("ACME.SIM", 59.4m), ("BETA.SIM", 79.2m)]);
    }

    [Test]
    public void Plan_ATargetItWouldBuy_ThatIsNotActive_FailsWithInstrumentInactive()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m, isActive: false)], orderChecks: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.Failed);
        plan.Code.ShouldBe("instrument_inactive");
        plan.Message.ShouldBe("ACME.SIM is disabled. A rebalance trades only active instruments.");
        plan.Trades.ShouldBeEmpty();
    }

    [Test]
    public void Plan_AHoldingItWouldSell_ThatIsNotActive_FailsWithInstrumentInactive()
    {
        var plan = RebalancePlanner.Plan(Input(0m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)],
            holdings: [Holding(1, "OLD.SIM", "USD", 40m, 25m, isActive: false)],
            orderChecks: true));

        plan.Code.ShouldBe("instrument_inactive");
        plan.Message!.ShouldStartWith("OLD.SIM");
    }

    [Test]
    public void Plan_AnInactiveTarget_ItDoesNotTrade_DoesNotFail()
    {
        var plan = RebalancePlanner.Plan(Input(100m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m, isActive: false)],
            holdings: [Holding(2, "ACME.SIM", "USD", 99m, 100m, isActive: false)],
            orderChecks: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.NoChange);
    }

    [Test]
    public void Plan_APriceMoreThan4DaysOld_OnATradedInstrument_FailsWithStalePrice()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m, priceDate: Today.AddDays(-5))], orderChecks: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.Failed);
        plan.Code.ShouldBe("stale_price");
        plan.Message.ShouldBe("The latest price of ACME.SIM is from 2026-11-18. An order needs a price from the last 4 days.");
    }

    [Test]
    public void Plan_APrice4DaysOld_IsFresh()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m, priceDate: Today.AddDays(-4))], orderChecks: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.Success);
    }

    [Test]
    public void Plan_APriceWithNoDate_WithTheOrderChecks_FailsWithMissingPrice()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m, noPriceDate: true)], orderChecks: true));

        plan.Code.ShouldBe("missing_price");
    }

    [Test]
    public void Plan_ATargetQuotedInAnotherCurrency_FailsWithCurrencyMismatch()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 50m, 100m), Target(3, "BHP.AX", "AUD", 50m, 40m, quoteToBase: 0.65m, quoteToAccount: 0.65m)],
            orderChecks: true, accountCurrency: "NZD", baseCurrency: "NZD"));

        plan.Status.ShouldBe(RebalancePlanOutcome.Failed);
        plan.Code.ShouldBe("currency_mismatch");
        plan.Message.ShouldBe(
            "BHP.AX is quoted in AUD and this account is in NZD. A rebalance buys only instruments quoted in the account's currency.");
    }

    [Test]
    public void Plan_AHoldingQuotedInAnotherCurrency_IsStillSold()
    {
        var plan = RebalancePlanner.Plan(Input(0m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)],
            holdings: [Holding(1, "BHP.AX", "AUD", 40m, 40m, quoteToBase: 0.65m, quoteToAccount: 0.65m)],
            orderChecks: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.Success);
        plan.Trades.First().ShouldSatisfyAllConditions(
            trade => trade.Symbol.ShouldBe("BHP.AX"),
            trade => trade.Buy.ShouldBeFalse(),
            trade => trade.Quantity.ShouldBe(40m));
    }

    [Test]
    public void Plan_SizedByReservation_WithAFeeSchedule_NeverReservesMoreThanTheCash()
    {
        var schedule = Schedule(minimum: 5m);
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 60m, 100m), Target(3, "BETA.SIM", "USD", 40m, 50m)],
            fees: Fees(schedule), orderChecks: true, byReservation: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.Success);
        var buys = plan.Trades;
        buys.ShouldAllBe(trade => trade.Buy);
        Reserved(buys, schedule).ShouldBeLessThanOrEqualTo(10000m);
        Reserved(buys, schedule).ShouldBeGreaterThan(9990m);
        // R16, §8.1: the first buy wave invests about 95 % of what the plan wants.
        buys[0].Quantity.ShouldBeInRange(56.5m, 57.2m);
        buys[1].Quantity.ShouldBeInRange(75.4m, 76.2m);
    }

    [Test]
    public void Plan_SizedByReservation_WithoutAFeeSchedule_NeverReservesMoreThanTheCash()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)], residual: 0m, orderChecks: true, byReservation: true));

        var buy = plan.Trades.ShouldHaveSingleItem();
        Reserved(plan.Trades, null).ShouldBeLessThanOrEqualTo(10000m);
        buy.Quantity.ShouldBeInRange(95.2m, 95.25m);
    }

    [Test]
    public void Plan_SizedByReservation_WithAFeeCap_NeverReservesMoreThanTheCash()
    {
        var schedule = Schedule(minimum: 5m, maximum: 12m);
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)], residual: 0m,
            fees: Fees(schedule), orderChecks: true, byReservation: true));

        Reserved(plan.Trades, schedule).ShouldBeLessThanOrEqualTo(10000m);
        Reserved(plan.Trades, schedule).ShouldBeGreaterThan(9980m);
    }

    [Test]
    public void Plan_SizedByReservation_IgnoresTheProceedsOfSells()
    {
        var plan = RebalancePlanner.Plan(Input(1000m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)],
            holdings: [Holding(1, "OLD.SIM", "USD", 40m, 25m)],
            residual: 0m, orderChecks: true, byReservation: true));

        Reserved(plan.Trades.Where(trade => trade.Buy).ToList(), null).ShouldBeLessThanOrEqualTo(1000m);
    }

    [Test]
    public void Plan_SizedByReservation_DropsABuyBelowTheMinimumTrade()
    {
        var plan = RebalancePlanner.Plan(Input(10000m,
            [Target(2, "ACME.SIM", "USD", 99m, 100m), Target(3, "BETA.SIM", "USD", 1m, 50m)],
            residual: 0m, minimumTrade: 99m, orderChecks: true, byReservation: true));

        plan.Trades.ShouldHaveSingleItem().Symbol.ShouldBe("ACME.SIM");
    }

    [Test]
    public void Plan_SizedByReservation_WithNoCash_IsNoChange()
    {
        var plan = RebalancePlanner.Plan(Input(50m,
            [Target(2, "ACME.SIM", "USD", 100m, 100m)],
            holdings: [Holding(2, "ACME.SIM", "USD", 100m, 100m)],
            residual: 0m, minimumTrade: 49m, orderChecks: true, byReservation: true));

        plan.Status.ShouldBe(RebalancePlanOutcome.NoChange);
    }

    private static decimal Reserved(IReadOnlyList<RebalanceTrade> buys, TradeFeeSchedule? schedule) =>
        buys.Sum(buy =>
        {
            var gross = OrderRules.MaximumGross(buy.Quantity, OrderRules.PriceLimit(OrderSide.Buy, buy.Price));
            return gross + new OrderFeeTerms(schedule, 2).FeeFor(gross);
        });

    private static TradeFeeSchedule Schedule(decimal minimum, decimal? maximum = null)
    {
        var schedule = TradeFeeSchedule.Create(
            1, Currency.Create("USD", "USD", 2), new DateOnly(2026, 11, 1), minimum, maximum,
            [new FeeBand(0m, 0.5m)], new DateOnly(2026, 10, 15));
        schedule.Id = 9;
        return schedule;
    }

    private static IReadOnlyDictionary<string, PlannerFeeSchedule> Fees(TradeFeeSchedule schedule) =>
        new Dictionary<string, PlannerFeeSchedule> { [schedule.QuoteCurrency] = new(schedule, 2) };

    private static RebalanceInput Input(
        decimal cash,
        PlannerTarget[] targets,
        PlannerHolding[]? holdings = null,
        decimal residual = 1m,
        decimal minimumTrade = 100m,
        IReadOnlyDictionary<string, PlannerFeeSchedule>? fees = null,
        bool orderChecks = false,
        bool byReservation = false,
        string accountCurrency = "USD",
        string baseCurrency = "USD") =>
        new(baseCurrency, accountCurrency, cash, 1m, minimumTrade, residual, holdings ?? [], targets, fees,
            OrderChecks: orderChecks, SizeBuysByReservation: byReservation, Today: Today);

    private static PlannerHolding Holding(
        int id, string symbol, string quote, decimal quantity, decimal price,
        decimal? quoteToBase = null, decimal? quoteToAccount = null, bool isActive = true) =>
        new(id, symbol, quote, quantity, price, quoteToBase, quoteToAccount, isActive, Today);

    private static PlannerTarget Target(
        int id, string symbol, string quote, decimal weight, decimal price,
        decimal? quoteToBase = null, decimal? quoteToAccount = null, bool isActive = true, DateOnly? priceDate = null,
        bool noPriceDate = false) =>
        new(id, symbol, quote, weight, price, quoteToBase, quoteToAccount, isActive, noPriceDate ? null : priceDate ?? Today);
}
