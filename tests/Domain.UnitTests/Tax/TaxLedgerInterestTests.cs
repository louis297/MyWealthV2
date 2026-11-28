using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Income-summary R3: the interest of a Customer from the facts of their ledger, in the order of
/// tax-year-events R8.</summary>
public class TaxLedgerInterestTests
{
    [Test]
    public void APostingOfAnInterestRunAndAHandEnteredInterest_GiveAnItemEach_InTheOrderOfBookedAtThenId()
    {
        var later = Posting(90, I1.BookedAt, 1.00m, 0m, null);

        var items = TaxLedger.BuildInterest([I3, later, T4, I1]);

        items.Select(item => (item.Ordinal, item.TransactionId)).ShouldBe([(0, 90), (1, 101), (2, 104)]);
    }

    [Test]
    public void AHandEnteredInterest_HasItsAccountItsFiguresAndNoPeriod()
    {
        var item = TaxLedger.BuildInterest([I1]).ShouldHaveSingleItem();

        (item.TransactionId, item.TransactionPublicId, item.BookedAt).ShouldBe((101, IdOf(101), I1.BookedAt));
        (item.AccountId, item.CustodyMode, item.Currency).ShouldBe((A2, CustodyMode.External, "NZD"));
        (item.Gross, item.Rwt, item.Nrwt, item.ForeignTax).ShouldBe((120.00m, 21.00m, 0m, 0m));
        item.Period.ShouldBeNull();
    }

    [Test]
    public void APostingOfAnInterestRun_HasItsPeriod()
    {
        var item = TaxLedger.BuildInterest([I3]).ShouldHaveSingleItem();

        (item.AccountId, item.CustodyMode, item.Currency).ShouldBe((A1, CustodyMode.Custodial, "USD"));
        (item.Gross, item.Rwt).ShouldBe((4.10m, 1.35m));
        item.Period.ShouldBe(new LedgerInterest(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 31));
    }

    [Test]
    public void AReversedInterestAndItsReversal_GiveNone()
    {
        var reversed = Fact(110, Utc(2025, 8, 1, 0, 0), TransactionType.Interest, [Cash(A2, CashLegRole.Principal, 50.00m)], [], reversed: true);
        var reversal = Fact(111, Utc(2025, 8, 2, 0, 0), TransactionType.Reversal, [Cash(A2, CashLegRole.Principal, -50.00m)], []);

        TaxLedger.BuildInterest([reversed, reversal, I1]).Select(item => item.TransactionId).ShouldBe([101]);
    }

    [Test]
    public void AnInterestWithTheThreeTaxKinds_HasEachAsAPositiveFigure()
    {
        var fact = Fact(120, Utc(2025, 8, 1, 0, 0), TransactionType.Interest,
            [
                Cash(A2, CashLegRole.Principal, 200.00m),
                Cash(A2, CashLegRole.Tax, -20.00m, TaxKind.Rwt),
                Cash(A2, CashLegRole.Tax, -3.00m, TaxKind.Nrwt),
                Cash(A2, CashLegRole.Tax, -7.50m, TaxKind.ForeignWithholding)
            ],
            []);

        var item = TaxLedger.BuildInterest([fact]).ShouldHaveSingleItem();

        (item.Gross, item.Rwt, item.Nrwt, item.ForeignTax).ShouldBe((200.00m, 20.00m, 3.00m, 7.50m));
    }

    [Test]
    public void APrincipalBelowZero_IsKeptSigned() =>
        TaxLedger.BuildInterest([I2]).ShouldHaveSingleItem().Gross.ShouldBe(-8.00m);

    [Test]
    public void ATypeThatIsNotInterest_GivesNoItem() =>
        TaxLedger.BuildInterest([T1, T4, D1, Fact(130, Utc(2025, 8, 1, 0, 0), TransactionType.TransferIn, [Cash(A2, CashLegRole.Principal, 9.00m)], [])])
            .ShouldBeEmpty();

    [Test]
    public void Build_StillGivesNoEventForAnInterest() =>
        TaxLedger.Build([I1, I2, I3]).ShouldBeEmpty();
}
