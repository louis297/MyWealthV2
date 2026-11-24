using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R44: the dividends received on a client money account in its bank reconciliation.</summary>
public class DividendReconciliationTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 17, 0, 3, TimeSpan.Zero);

    [Test]
    public void TheTwoKinds_HaveTheirClassAndValue()
    {
        BankReconciliationItem.ClassOf(ReconciliationItemKind.DividendsNotPosted).ShouldBe(ReconciliationItemClass.Timing);
        BankReconciliationItem.ClassOf(ReconciliationItemKind.DividendDifference).ShouldBe(ReconciliationItemClass.Known);
        ((int)ReconciliationItemKind.DividendsNotPosted, (int)ReconciliationItemKind.DividendDifference).ShouldBe((20, 21));
    }

    [Test]
    public void ADividendReceivedWithAPendingEntitlement_IsDividendsNotPosted_ForWhatIsNotCreditedYet()
    {
        var run = Compute(Inputs(bank: 93.50m, custodial: 67.00m) with
        {
            Dividends = [new ReconciliationDividend(93.50m, 67.00m, HasPending: true)]
        });

        var item = run.Items.ShouldHaveSingleItem();
        (item.Kind, item.Class, item.Amount, item.Count)
            .ShouldBe((ReconciliationItemKind.DividendsNotPosted, ReconciliationItemClass.Timing, 26.50m, 1));
        run.Status.ShouldBe(BankReconciliationStatus.Agreed);
    }

    [Test]
    public void PaidDividends_WithARest_AreOneDividendDifference_CountingTheDividendsWithARest()
    {
        var run = Compute(Inputs(bank: 193.54m, custodial: 193.50m) with
        {
            Dividends =
            [
                new ReconciliationDividend(93.54m, 93.50m, HasPending: false),
                new ReconciliationDividend(100.00m, 100.00m, HasPending: false)
            ]
        });

        var item = run.Items.ShouldHaveSingleItem();
        (item.Kind, item.Class, item.Amount, item.Count)
            .ShouldBe((ReconciliationItemKind.DividendDifference, ReconciliationItemClass.Known, 0.04m, 1));
        run.Items.Sum(i => i.Amount).ShouldBe(run.Difference!.Value);
    }

    [Test]
    public void APaidDividendWhoseAmountsAgree_IsNoItem()
    {
        var run = Compute(Inputs(bank: 93.50m, custodial: 93.50m) with
        {
            Dividends = [new ReconciliationDividend(93.50m, 93.50m, HasPending: false)]
        });

        run.Items.ShouldBeEmpty();
    }

    private static BankReconciliation Compute(ReconciliationInputs inputs) =>
        BankReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, null, At);

    private static ReconciliationInputs Inputs(decimal bank, decimal custodial) => new()
    {
        TenantId = 3,
        ClientMoneyAccountId = 4,
        Currency = "USD",
        StatementBalance = bank,
        BalanceAt = At,
        OpeningBalance = 0m,
        StatementLinesTotal = bank,
        CustodialCash = custodial
    };
}
