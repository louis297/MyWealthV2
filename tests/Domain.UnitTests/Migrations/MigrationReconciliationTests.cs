using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Migrations;

/// <summary>Migration batches R32: funding of a batch not posted is a timing difference; a posted batch is explained
/// by its funding lines.</summary>
public class MigrationReconciliationTests
{
    private const int Account = 4;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 17, 0, 3, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);

    [Test]
    public void MigrationNotPosted_IsATimingDifference()
    {
        BankReconciliationItem.ClassOf(ReconciliationItemKind.MigrationNotPosted).ShouldBe(ReconciliationItemClass.Timing);
        ((int)ReconciliationItemKind.MigrationNotPosted).ShouldBe(19);
    }

    [Test]
    public void AFundingLineOfABatchNotPosted_IsMigrationNotPosted_WithTheLine_AndTheRunAgrees()
    {
        var run = Compute(Inputs(bank: 250000m, custodial: 0m) with
        {
            Lines = [new ReconciliationLine(11, BankLineDirection.Credit, Day, 250000m, BankStatementLineStatus.Matched, null, null,
                MigrationNotPosted: true)]
        });

        var item = run.Items.ShouldHaveSingleItem();
        (item.Kind, item.Class, item.Amount, item.Count, item.StatementLineId)
            .ShouldBe((ReconciliationItemKind.MigrationNotPosted, ReconciliationItemClass.Timing, 250000m, 1, (int?)11));
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
        run.Items.Sum(i => i.Amount).ShouldBe(run.Difference!.Value);
    }

    [Test]
    public void AMatchedLineOfAPostedBatch_IsNoItem_AndItsOpeningCashThroughThisAccount_IsNoItem()
    {
        var run = Compute(Inputs(bank: 250000m, custodial: 250000m) with
        {
            Lines = [new ReconciliationLine(11, BankLineDirection.Credit, Day, 250000m, BankStatementLineStatus.Matched, null, null)],
            Ledger = [new ReconciliationLedgerCash(TransactionType.Opening, null, Account, 250000m, 2)]
        });

        run.Items.ShouldBeEmpty();
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void OpeningCashFundedThroughAnotherAccount_IsOtherClientMoneyAccount_AndTheItemsAddUp()
    {
        var run = Compute(Inputs(bank: 200000m, custodial: 250000m) with
        {
            Ledger =
            [
                new ReconciliationLedgerCash(TransactionType.Opening, null, Account, 200000m, 2),
                new ReconciliationLedgerCash(TransactionType.Opening, null, 9, 50000m, 2)
            ]
        });

        var item = run.Items.ShouldHaveSingleItem();
        (item.Kind, item.OtherClientMoneyAccountId, item.Amount)
            .ShouldBe((ReconciliationItemKind.OtherClientMoneyAccount, (int?)9, -50000m));
        run.Items.Sum(i => i.Amount).ShouldBe(run.Difference!.Value);
        run.Items.ShouldNotContain(i => i.Kind == ReconciliationItemKind.OtherLedgerCash);
    }

    private static BankReconciliation Compute(ReconciliationInputs inputs) =>
        BankReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, null, At);

    private static ReconciliationInputs Inputs(decimal bank, decimal custodial) => new()
    {
        TenantId = 3,
        ClientMoneyAccountId = Account,
        Currency = "USD",
        StatementBalance = bank,
        BalanceAt = At,
        OpeningBalance = 0m,
        StatementLinesTotal = bank,
        CustodialCash = custodial
    };
}
