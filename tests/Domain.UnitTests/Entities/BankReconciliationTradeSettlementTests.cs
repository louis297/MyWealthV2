using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Trade-settlement R22–R25: the reconciliation items of order fills that settle through the trust
/// account, and the open settlements of a run.</summary>
public class BankReconciliationTradeSettlementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);

    [TestCase(ReconciliationItemKind.UnsettledTrades, ReconciliationItemClass.Timing)]
    [TestCase(ReconciliationItemKind.TradesNeedingAction, ReconciliationItemClass.Break)]
    [TestCase(ReconciliationItemKind.UnbookedFills, ReconciliationItemClass.Break)]
    [TestCase(ReconciliationItemKind.TradeFees, ReconciliationItemClass.Known)]
    public void EachNewKind_HasItsClass(ReconciliationItemKind kind, ReconciliationItemClass itemClass)
    {
        BankReconciliationItem.ClassOf(kind).ShouldBe(itemClass);
    }

    [Test]
    public void ABuyWaitingForItsSettlement_IsTimingAndItsFeeKnown_AndTheRunAgrees()
    {
        // 5 000 received; a buy of 400.48 with a fee of 5.00 posted, not paid to the broker yet.
        var run = Compute(Inputs(bank: 5000m, custodial: 4594.52m) with
        {
            UnsettledTrades = -400.48m,
            UnsettledTradesCount = 1,
            TradeFees = -5m,
            TradeFeesCount = 1
        });

        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
        Items(run).ShouldBe([
            (ReconciliationItemKind.UnsettledTrades, ReconciliationItemClass.Timing, 400.48m, 1),
            (ReconciliationItemKind.TradeFees, ReconciliationItemClass.Known, 5m, 1)
        ]);
        run.Items.Sum(item => item.Amount).ShouldBe(run.Difference!.Value);
    }

    [Test]
    public void TradesNeedingAction_IsABreak()
    {
        var run = Compute(Inputs(bank: 5000m, custodial: 4594.52m) with
        {
            TradesNeedingAction = -400.48m,
            TradesNeedingActionCount = 1,
            TradeFees = -5m,
            TradeFeesCount = 1
        });

        run.Status.ShouldBe(BankReconciliationStatus.Breaks);
        Items(run).First().ShouldBe((ReconciliationItemKind.TradesNeedingAction, ReconciliationItemClass.Break, 400.48m, 1));
        run.Items.Sum(item => item.Amount).ShouldBe(run.Difference!.Value);
    }

    [Test]
    public void APendingFillInACompletedSettlement_IsUnbookedFills_ABreak_ThatExplainsTheBanksMove()
    {
        // The broker was paid 1 100.00 for a fill the ledger has not booked yet.
        var run = Compute(Inputs(bank: 3900m, custodial: 5000m, linesTotal: 3900m) with
        {
            UnbookedFills = -1100m,
            UnbookedFillsCount = 1
        });

        run.Status.ShouldBe(BankReconciliationStatus.Breaks);
        Items(run).ShouldBe([(ReconciliationItemKind.UnbookedFills, ReconciliationItemClass.Break, -1100m, 1)]);
    }

    [Test]
    public void ASettlementThatCompleted_LeavesOnlyTheFee()
    {
        var run = Compute(Inputs(bank: 4599.52m, custodial: 4594.52m, linesTotal: 4599.52m) with { TradeFees = -5m, TradeFeesCount = 1 });

        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
        Items(run).ShouldBe([(ReconciliationItemKind.TradeFees, ReconciliationItemClass.Known, 5m, 1)]);
    }

    [Test]
    public void TheOpenSettlements_AreRecorded_AndAreNotItems()
    {
        var run = Compute(Inputs(bank: 5000m, custodial: 5000m) with { OpenSettlementsCount = 2, OpenSettlementsAmount = -1001.50m });

        (run.OpenSettlementsCount, run.OpenSettlementsAmount).ShouldBe((2, -1001.50m));
        run.Items.ShouldBeEmpty();

        var noBalance = Compute(Inputs(bank: null, custodial: 5000m) with { OpenSettlementsCount = 1, OpenSettlementsAmount = 990m });
        (noBalance.OpenSettlementsCount, noBalance.OpenSettlementsAmount).ShouldBe((1, 990m));
    }

    private static BankReconciliation Compute(ReconciliationInputs inputs) =>
        BankReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, null, At);

    private static IEnumerable<(ReconciliationItemKind, ReconciliationItemClass, decimal, int)> Items(BankReconciliation run) =>
        run.Items.Select(item => (item.Kind, item.Class, item.Amount, item.Count));

    private static ReconciliationInputs Inputs(decimal? bank, decimal custodial, decimal? linesTotal = null) => new()
    {
        TenantId = 3,
        ClientMoneyAccountId = 40,
        Currency = "USD",
        StatementBalance = bank,
        BalanceAt = bank is null ? null : At,
        OpeningBalance = bank is null ? null : 0m,
        StatementLinesTotal = linesTotal ?? bank ?? 0m,
        CustodialCash = custodial
    };
}
