using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Bank-reconciliation R8–R13: the items, the status and the totals of a run.</summary>
public class BankReconciliationTests
{
    private const int Account = 4;
    private static readonly DateTimeOffset At = new(2026, 11, 22, 17, 0, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset BalanceAt = new(2026, 11, 22, 16, 59, 7, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 22);

    [Test]
    public void NoStatementBalance_IsNoBankBalance_WithTheCustodialCashOnly()
    {
        var run = Compute(Inputs(bank: null, opening: null, linesTotal: 500m, custodial: 120m) with
        {
            Lines = [Line(1, BankStatementLineStatus.Unmatched, 500m)],
            Ledger = [new ReconciliationLedgerCash(TransactionType.Buy, null, null, -10m, 1)],
            ReservedCash = 30m,
            PaymentsInFlightCount = 1,
            PaymentsInFlightAmount = 30m,
            FeedLastFailure = BankFeedFailure.Unavailable
        });

        run.Status.ShouldBe(BankReconciliationStatus.NoBankBalance);
        run.CustodialCash.ShouldBe(120m);
        run.BankBalance.ShouldBeNull();
        run.BalanceAt.ShouldBeNull();
        run.OpeningBalance.ShouldBeNull();
        run.StatementLinesTotal.ShouldBeNull();
        run.Difference.ShouldBeNull();
        run.Items.ShouldBeEmpty();
        (run.BreakCount, run.TimingCount, run.KnownCount).ShouldBe((0, 0, 0));
        (run.BreakTotal, run.TimingTotal, run.KnownTotal).ShouldBe((0m, 0m, 0m));
        run.ReservedCash.ShouldBe(30m);
        run.PaymentsInFlightCount.ShouldBe(1);
        run.PaymentsInFlightAmount.ShouldBe(30m);
        run.FeedLastFailure.ShouldBe(BankFeedFailure.Unavailable);
    }

    [Test]
    public void BankAndCustodialCashTheSame_WithEveryLineMatched_IsAgreedWithNoItems()
    {
        var run = Compute(Inputs(bank: 700m, opening: 0m, linesTotal: 700m, custodial: 700m) with
        {
            Lines = [Line(1, BankStatementLineStatus.Matched, 1000m), Line(2, BankStatementLineStatus.Matched, 300m, BankLineDirection.Debit)],
            Ledger = [new ReconciliationLedgerCash(TransactionType.TransferIn, null, Account, 1000m, 1),
                      new ReconciliationLedgerCash(TransactionType.TransferOut, null, Account, -300m, 1)]
        });

        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
        run.Items.ShouldBeEmpty();
        run.BankBalance.ShouldBe(700m);
        run.BalanceAt.ShouldBe(BalanceAt);
        run.OpeningBalance.ShouldBe(0m);
        run.StatementLinesTotal.ShouldBe(700m);
        run.Difference.ShouldBe(0m);
    }

    [Test]
    public void TheRun_RecordsItsAccountTriggerTimeAndTheInformation()
    {
        var run = BankReconciliation.Compute(
            Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: 0m) with
            {
                ReservedCash = 300m,
                PaymentsInFlightCount = 2,
                PaymentsInFlightAmount = 450m,
                FeedLastSucceededAt = BalanceAt,
                FeedLastFailedAt = At,
                FeedLastFailure = BankFeedFailure.Timeout
            },
            ReconciliationTrigger.Manual,
            ranByUserId: 9,
            At);

        run.PublicId.ShouldNotBe(Guid.Empty);
        run.TenantId.ShouldBe(3);
        run.ClientMoneyAccountId.ShouldBe(Account);
        run.Currency.ShouldBe("USD");
        run.Trigger.ShouldBe(ReconciliationTrigger.Manual);
        run.RanByUserId.ShouldBe(9);
        run.RanAt.ShouldBe(At);
        run.ReservedCash.ShouldBe(300m);
        run.PaymentsInFlightCount.ShouldBe(2);
        run.PaymentsInFlightAmount.ShouldBe(450m);
        run.FeedLastSucceededAt.ShouldBe(BalanceAt);
        run.FeedLastFailedAt.ShouldBe(At);
        run.FeedLastFailure.ShouldBe(BankFeedFailure.Timeout);
    }

    [Test]
    public void AScheduledRunNamingAPerson_OrAManualRunNamingNone_IsRefused()
    {
        var inputs = Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: 0m);

        Should.Throw<DomainException>(() => BankReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, 9, At));
        Should.Throw<DomainException>(() => BankReconciliation.Compute(inputs, ReconciliationTrigger.Manual, null, At));
    }

    [TestCase(BankStatementLineStatus.Unmatched, ReconciliationItemKind.Suspense, ReconciliationItemClass.Break, BankReconciliationStatus.Breaks)]
    [TestCase(BankStatementLineStatus.Received, ReconciliationItemKind.NotProcessed, ReconciliationItemClass.Timing, BankReconciliationStatus.Agreed)]
    [TestCase(BankStatementLineStatus.ReturnRequested, ReconciliationItemKind.ReturnInProgress, ReconciliationItemClass.Timing, BankReconciliationStatus.Agreed)]
    [TestCase(BankStatementLineStatus.ReturnSent, ReconciliationItemKind.ReturnInProgress, ReconciliationItemClass.Timing, BankReconciliationStatus.Agreed)]
    [TestCase(BankStatementLineStatus.Ignored, ReconciliationItemKind.Ignored, ReconciliationItemClass.Known, BankReconciliationStatus.Agreed)]
    public void ALineInAnItemStatus_IsOneItemOfItsKindAndClass(
        BankStatementLineStatus status,
        ReconciliationItemKind kind,
        ReconciliationItemClass itemClass,
        BankReconciliationStatus runStatus)
    {
        var run = Compute(Inputs(bank: 500m, opening: 0m, linesTotal: 500m, custodial: 0m) with
        {
            Lines = [Line(11, status, 500m)]
        });

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(kind);
        item.Class.ShouldBe(itemClass);
        item.Amount.ShouldBe(500m);
        item.Count.ShouldBe(1);
        item.StatementLineId.ShouldBe(11);
        item.Position.ShouldBe(1);
        item.TenantId.ShouldBe(3);
        run.Status.ShouldBe(runStatus);
    }

    [Test]
    public void ADebitLine_IsANegativeItem()
    {
        var run = Compute(Inputs(bank: -15m, opening: 0m, linesTotal: -15m, custodial: 0m) with
        {
            Lines = [Line(11, BankStatementLineStatus.Ignored, 15m, BankLineDirection.Debit, note: "Account fee November")]
        });

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ReconciliationItemKind.Ignored);
        item.Amount.ShouldBe(-15m);
        item.Direction.ShouldBe(BankLineDirection.Debit);
        item.Note.ShouldBe("Account fee November");
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [TestCase(BankStatementLineStatus.Matched)]
    [TestCase(BankStatementLineStatus.Returned)]
    public void AMatchedOrReturnedLine_IsNoItem(BankStatementLineStatus status)
    {
        var run = Compute(Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: 0m) with
        {
            Lines = [Line(11, status, 500m)]
        });

        run.Items.Where(item => item.StatementLineId is not null).ShouldBeEmpty();
    }

    [Test]
    public void ALineItem_CarriesTheLineAsItWas_WithTheReasonOnlyWhenUnmatched()
    {
        var run = Compute(Inputs(bank: 800m, opening: 0m, linesTotal: 800m, custodial: 0m) with
        {
            Lines =
            [
                Line(11, BankStatementLineStatus.Unmatched, 500m, reason: UnmatchedReason.UnknownReference),
                Line(12, BankStatementLineStatus.Ignored, 300m, reason: UnmatchedReason.NoReference, note: "Bank interest November")
            ]
        });

        var suspense = run.Items.Single(item => item.Kind == ReconciliationItemKind.Suspense);
        suspense.Direction.ShouldBe(BankLineDirection.Credit);
        suspense.ValueDate.ShouldBe(Day);
        suspense.LineStatus.ShouldBe(BankStatementLineStatus.Unmatched);
        suspense.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReference);
        suspense.Note.ShouldBeNull();
        var ignored = run.Items.Single(item => item.Kind == ReconciliationItemKind.Ignored);
        ignored.LineStatus.ShouldBe(BankStatementLineStatus.Ignored);
        ignored.UnmatchedReason.ShouldBeNull();
        ignored.Note.ShouldBe("Bank interest November");
    }

    [TestCase(TransactionType.Buy, ReconciliationItemKind.Trades)]
    [TestCase(TransactionType.Sell, ReconciliationItemKind.Trades)]
    [TestCase(TransactionType.FxConversion, ReconciliationItemKind.FxConversions)]
    [TestCase(TransactionType.Adjustment, ReconciliationItemKind.Adjustments)]
    [TestCase(TransactionType.TransferIn, ReconciliationItemKind.OtherLedgerCash)]
    [TestCase(TransactionType.Dividend, ReconciliationItemKind.OtherLedgerCash)]
    [TestCase(TransactionType.Opening, ReconciliationItemKind.OtherLedgerCash)]
    public void LedgerCashThatNeverPassedThroughATrustAccount_IsAKnownItemByType(TransactionType type, ReconciliationItemKind kind)
    {
        var run = Compute(Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: -1175.2m) with
        {
            Ledger = [new ReconciliationLedgerCash(type, null, null, -1175.2m, 4)]
        });

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(kind);
        item.Class.ShouldBe(ReconciliationItemClass.Known);
        item.Amount.ShouldBe(1175.2m);
        item.Count.ShouldBe(4);
        item.StatementLineId.ShouldBeNull();
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void AReversal_CountsUnderItsOriginalsType_AndGroupsAddUp()
    {
        var run = Compute(Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: -60m) with
        {
            Ledger =
            [
                new ReconciliationLedgerCash(TransactionType.Buy, null, null, -100m, 2),
                new ReconciliationLedgerCash(TransactionType.Reversal, TransactionType.Buy, null, 40m, 1),
                new ReconciliationLedgerCash(TransactionType.Sell, null, null, 0m, 1)
            ]
        });

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ReconciliationItemKind.Trades);
        item.Amount.ShouldBe(60m);
        item.Count.ShouldBe(4);
    }

    [Test]
    public void ALedgerGroupThatSumsToZero_IsLeftOut()
    {
        var run = Compute(Inputs(bank: 0m, opening: 0m, linesTotal: 0m, custodial: 0m) with
        {
            Ledger =
            [
                new ReconciliationLedgerCash(TransactionType.InternalTransfer, null, null, 0m, 1),
                new ReconciliationLedgerCash(TransactionType.Adjustment, null, null, 25m, 1),
                new ReconciliationLedgerCash(TransactionType.Reversal, TransactionType.Adjustment, null, -25m, 1),
                new ReconciliationLedgerCash(TransactionType.TransferIn, null, 8, 10m, 1),
                new ReconciliationLedgerCash(TransactionType.Reversal, TransactionType.TransferIn, 8, -10m, 1)
            ]
        });

        run.Items.ShouldBeEmpty();
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void LedgerCashThroughThisAccount_IsNoItem_AndThroughAnotherIsOneItemPerAccount()
    {
        var run = Compute(Inputs(bank: 500m, opening: 0m, linesTotal: 500m, custodial: 750m) with
        {
            Lines = [Line(11, BankStatementLineStatus.Matched, 500m)],
            Ledger =
            [
                new ReconciliationLedgerCash(TransactionType.TransferIn, null, Account, 500m, 1),
                new ReconciliationLedgerCash(TransactionType.TransferIn, null, 9, 200m, 2),
                new ReconciliationLedgerCash(TransactionType.TransferOut, null, 9, -50m, 1),
                new ReconciliationLedgerCash(TransactionType.TransferIn, null, 7, 100m, 1)
            ]
        });

        run.Items.Select(item => (item.Kind, item.OtherClientMoneyAccountId, item.Amount, item.Count))
            .ShouldBe([
                (ReconciliationItemKind.OtherClientMoneyAccount, (int?)7, -100m, 1),
                (ReconciliationItemKind.OtherClientMoneyAccount, (int?)9, -150m, 3)
            ]);
        run.Items.ShouldAllBe(item => item.Class == ReconciliationItemClass.Known);
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void AnOpeningBalance_IsAKnownItem()
    {
        var run = Compute(Inputs(bank: 1100m, opening: 1000m, linesTotal: 100m, custodial: 100m));

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ReconciliationItemKind.OpeningBalance);
        item.Class.ShouldBe(ReconciliationItemClass.Known);
        item.Amount.ShouldBe(1000m);
        item.Count.ShouldBe(1);
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void ABankBalanceTheLinesDoNotExplain_IsAStatementDifferenceBreak()
    {
        var run = Compute(Inputs(bank: 95m, opening: 0m, linesTotal: 100m, custodial: 100m));

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ReconciliationItemKind.StatementDifference);
        item.Class.ShouldBe(ReconciliationItemClass.Break);
        item.Amount.ShouldBe(-5m);
        run.Status.ShouldBe(BankReconciliationStatus.Breaks);
    }

    [Test]
    public void WhatNoLineOrLegExplains_IsAnUnexplainedBreak()
    {
        var run = Compute(Inputs(bank: 100m, opening: 0m, linesTotal: 100m, custodial: 90m) with
        {
            Lines = [Line(11, BankStatementLineStatus.Matched, 100m)],
            Ledger = [new ReconciliationLedgerCash(TransactionType.TransferIn, null, Account, 90m, 1)]
        });

        var item = run.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ReconciliationItemKind.Unexplained);
        item.Class.ShouldBe(ReconciliationItemClass.Break);
        item.Amount.ShouldBe(10m);
        run.Status.ShouldBe(BankReconciliationStatus.Breaks);
    }

    [Test]
    public void ItemsAreOrderedByClassThenKindThenValueDateAndLine_AndAddUpToTheDifference_WithTotalsPerClass()
    {
        // Bank 12512.40 after an opening of 0: lines 11–15 and a missing 15.00 fee; Custodial cash 10824.80.
        var run = Compute(Inputs(bank: 12497.40m, opening: 0m, linesTotal: 12512.40m, custodial: 10824.80m) with
        {
            Lines =
            [
                Line(15, BankStatementLineStatus.Unmatched, 500m, valueDate: Day, reason: UnmatchedReason.UnknownReference),
                Line(14, BankStatementLineStatus.Unmatched, 40m, BankLineDirection.Debit, valueDate: Day.AddDays(-1), reason: UnmatchedReason.UnknownPayment),
                Line(13, BankStatementLineStatus.Received, 200m, valueDate: Day),
                Line(12, BankStatementLineStatus.Ignored, 12.40m, valueDate: Day.AddDays(-1), note: "Bank interest"),
                Line(16, BankStatementLineStatus.Unmatched, 60m, valueDate: Day, reason: UnmatchedReason.NoReference),
                Line(11, BankStatementLineStatus.Matched, 11740m)
            ],
            Ledger =
            [
                new ReconciliationLedgerCash(TransactionType.TransferIn, null, Account, 11740m, 3),
                new ReconciliationLedgerCash(TransactionType.Buy, null, null, -1175.20m, 4),
                new ReconciliationLedgerCash(TransactionType.Adjustment, null, null, 260m, 1)
            ]
        });

        run.Items.Select(item => (item.Position, item.Kind, item.StatementLineId)).ShouldBe([
            (1, ReconciliationItemKind.Unexplained, (int?)null),
            (2, ReconciliationItemKind.StatementDifference, null),
            (3, ReconciliationItemKind.Suspense, 14),
            (4, ReconciliationItemKind.Suspense, 15),
            (5, ReconciliationItemKind.Suspense, 16),
            (6, ReconciliationItemKind.NotProcessed, 13),
            (7, ReconciliationItemKind.Ignored, 12),
            (8, ReconciliationItemKind.Trades, null),
            (9, ReconciliationItemKind.Adjustments, null)
        ]);
        run.Difference.ShouldBe(1672.60m);
        run.Items.Sum(item => item.Amount).ShouldBe(run.Difference!.Value);
        run.Items.Single(item => item.Kind == ReconciliationItemKind.StatementDifference).Amount.ShouldBe(-15m);
        run.Items.Single(item => item.Kind == ReconciliationItemKind.Adjustments).Amount.ShouldBe(-260m);
        // 1672.60 = -15 + (500 - 40 + 60) + 200 + 12.40 + 1175.20 - 260 + unexplained, so unexplained = 40.
        run.Items.Single(item => item.Kind == ReconciliationItemKind.Unexplained).Amount.ShouldBe(40m);
        (run.BreakCount, run.BreakTotal).ShouldBe((5, 545m));
        (run.TimingCount, run.TimingTotal).ShouldBe((1, 200m));
        (run.KnownCount, run.KnownTotal).ShouldBe((3, 927.60m));
        run.Status.ShouldBe(BankReconciliationStatus.Breaks);
    }

    [TestCase(0, 0, 0)]
    [TestCase(250.5, -10, 0)]
    [TestCase(-80, 30, 1000)]
    public void TheItems_AlwaysAddUpToTheDifference(double bank, double custodial, double opening)
    {
        var run = Compute(Inputs(bank: (decimal)bank, opening: (decimal)opening, linesTotal: 120m, custodial: (decimal)custodial) with
        {
            Lines = [Line(1, BankStatementLineStatus.Unmatched, 100m), Line(2, BankStatementLineStatus.Received, 20m)],
            Ledger = [new ReconciliationLedgerCash(TransactionType.Sell, null, null, 7.5m, 1)]
        });

        run.Items.Sum(item => item.Amount).ShouldBe((decimal)bank - (decimal)custodial);
        run.Items.Select(item => item.Position).ShouldBe(Enumerable.Range(1, run.Items.Count));
        (run.BreakTotal + run.TimingTotal + run.KnownTotal).ShouldBe(run.Difference!.Value);
        (run.BreakCount + run.TimingCount + run.KnownCount).ShouldBe(run.Items.Count);
    }

    [TestCase(ReconciliationItemKind.Unexplained, ReconciliationItemClass.Break)]
    [TestCase(ReconciliationItemKind.StatementDifference, ReconciliationItemClass.Break)]
    [TestCase(ReconciliationItemKind.Suspense, ReconciliationItemClass.Break)]
    [TestCase(ReconciliationItemKind.NotProcessed, ReconciliationItemClass.Timing)]
    [TestCase(ReconciliationItemKind.ReturnInProgress, ReconciliationItemClass.Timing)]
    [TestCase(ReconciliationItemKind.OpeningBalance, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.Ignored, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.Trades, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.FxConversions, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.Adjustments, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.OtherLedgerCash, ReconciliationItemClass.Known)]
    [TestCase(ReconciliationItemKind.OtherClientMoneyAccount, ReconciliationItemClass.Known)]
    public void EachKind_HasOneClass(ReconciliationItemKind kind, ReconciliationItemClass itemClass) =>
        BankReconciliationItem.ClassOf(kind).ShouldBe(itemClass);

    private static BankReconciliation Compute(ReconciliationInputs inputs) =>
        BankReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, null, At);

    private static ReconciliationInputs Inputs(decimal? bank, decimal? opening, decimal linesTotal, decimal custodial) => new()
    {
        TenantId = 3,
        ClientMoneyAccountId = Account,
        Currency = "USD",
        StatementBalance = bank,
        BalanceAt = bank is null ? null : BalanceAt,
        OpeningBalance = opening,
        StatementLinesTotal = linesTotal,
        CustodialCash = custodial
    };

    private static ReconciliationLine Line(
        int id,
        BankStatementLineStatus status,
        decimal amount,
        BankLineDirection direction = BankLineDirection.Credit,
        DateOnly? valueDate = null,
        UnmatchedReason? reason = null,
        string? note = null) =>
        new(id, direction, valueDate ?? Day, amount, status, reason, note);
}
