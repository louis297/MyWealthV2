using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>External income R9, R22, R23: a hand <c>Interest</c> on an External account with the tax an outside payer
/// withheld (§1 examples E and F).</summary>
public class EnteredInterestTests
{
    private const int TenantId = 1;
    private const int AccountId = 7;
    private static readonly DateTimeOffset At = new(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

    private static Transaction Post(
        decimal gross,
        IReadOnlyList<EnteredTax> taxes,
        string currency = "NZD",
        string? memo = null,
        string? reference = null,
        TransactionSource? source = null) =>
        Transaction.PostEnteredInterest(
            TenantId, AccountId, currency, gross, taxes, At, memo, reference, source ?? TransactionSource.Manual);

    private static IEnumerable<(CashLegRole Role, decimal Amount, TaxKind? Kind)> Legs(Transaction transaction) =>
        transaction.CashLegs.Select(leg => (leg.Role, leg.Amount, leg.TaxDetail?.Kind));

    [Test]
    public void ExampleE_Rwt_IsATaxLegOnTheGross()
    {
        var transaction = Post(12.34m, [new EnteredTax(TaxKind.Rwt, 33m, 4.07m)], memo: "November interest");

        transaction.Type.ShouldBe(TransactionType.Interest);
        Legs(transaction).ShouldBe(
            [(CashLegRole.Principal, 12.34m, null), (CashLegRole.Tax, -4.07m, TaxKind.Rwt)], ignoreOrder: true);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(8.27m);
        var detail = transaction.TaxDetails.Single();
        (detail.Kind, detail.RatePercent, detail.BasisAmount, detail.TaxAmount, detail.Currency)
            .ShouldBe((TaxKind.Rwt, 33m, 12.34m, 4.07m, "NZD"));
        transaction.InterestDetail.ShouldBeNull();
        transaction.DividendDetail.ShouldBeNull();
        (transaction.SourceKind, transaction.Memo, transaction.BookedAt)
            .ShouldBe((TransactionSourceKind.Manual, "November interest", At));
        transaction.DomainEvents.OfType<TransactionPosted>().Count().ShouldBe(1);
    }

    [Test]
    public void ExampleF_ForeignWithholding_IsAcceptedOnAnInterest()
    {
        var transaction = Post(50.00m, [new EnteredTax(TaxKind.ForeignWithholding, 10m, 5.00m)], "AUD", reference: "STMT-11");

        Legs(transaction).ShouldBe(
        [
            (CashLegRole.Principal, 50.00m, null),
            (CashLegRole.Tax, -5.00m, TaxKind.ForeignWithholding)
        ], ignoreOrder: true);
        transaction.CashLegs.ShouldAllBe(leg => leg.Currency == "AUD" && leg.AccountId == AccountId);
        transaction.TaxDetails.Single().BasisAmount.ShouldBe(50.00m);
        transaction.Reference.ShouldBe("STMT-11");
    }

    [Test]
    public void NoTax_Throws() =>
        Should.Throw<DomainException>(() => Post(12.34m, []));

    [TestCase(0)]
    [TestCase(-12.34)]
    public void AGrossNotAbove0_Throws(decimal gross) =>
        Should.Throw<DomainException>(() => Post(gross, [new EnteredTax(TaxKind.Rwt, 33m, 1m)]));

    [Test]
    public void TaxesAboveTheGross_Throw() =>
        Should.Throw<DomainException>(() => Post(
                10.00m, [new EnteredTax(TaxKind.Rwt, 33m, 6m), new EnteredTax(TaxKind.ForeignWithholding, 50m, 5m)]))
            .Message.ShouldBe(EnteredTaxes.AboveGrossMessage);

    [Test]
    public void RwtWithNrwt_Throws() =>
        Should.Throw<DomainException>(() => Post(
                10.00m, [new EnteredTax(TaxKind.Rwt, 33m, 1m), new EnteredTax(TaxKind.Nrwt, 15m, 1m)]))
            .Message.ShouldBe(EnteredTaxes.RwtWithNrwtMessage);

    [Test]
    public void ASourceOtherThanManual_Throws() =>
        Should.Throw<DomainException>(() => Post(
            12.34m, [new EnteredTax(TaxKind.Rwt, 33m, 4.07m)], source: TransactionSource.InterestRun(Guid.NewGuid())));

    [Test]
    public void Reverse_CopiesTheTaxLegAndItsDetail()
    {
        var transaction = Post(12.34m, [new EnteredTax(TaxKind.Rwt, 33m, 4.07m)]);

        var reversal = transaction.Reverse(
            TransactionSource.Manual, At.AddMinutes(1), new Dictionary<int, decimal> { [AccountId] = 8.27m });

        Legs(reversal).ShouldBe(
            [(CashLegRole.Principal, -12.34m, null), (CashLegRole.Tax, 4.07m, TaxKind.Rwt)], ignoreOrder: true);
        reversal.TaxDetails.Single().BasisAmount.ShouldBe(12.34m);
        reversal.InterestDetail.ShouldBeNull();
    }
}
