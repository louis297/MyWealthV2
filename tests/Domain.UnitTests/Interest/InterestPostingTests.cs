using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Interest;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Interest;

/// <summary>Interest-run R22–R27: <c>Transaction.PostInterest</c>, the <c>Tax</c> cash leg, the tax and interest
/// details, and the source kind <c>InterestRun</c>.</summary>
public class InterestPostingTests
{
    private const int TenantId = 3;
    private const int AccountId = 11;
    private static readonly DateTimeOffset BookedAt = new(2027, 1, 1, 0, 30, 4, TimeSpan.Zero);
    private static readonly Guid RunId = Guid.Parse("7c2f0000-0000-0000-0000-0000000000a1");
    private static readonly InterestPeriod December = new(new DateOnly(2026, 12, 2), new DateOnly(2026, 12, 31), 30);
    private static readonly Withholding Rwt175 = new(TaxKind.Rwt, 17.5m, WithholdingBasis.Elected);

    private static Transaction Post(
        decimal accrued = 12.32876700m,
        Withholding? withholding = null,
        string currency = "USD",
        int decimalPlaces = 2,
        TransactionSource? source = null) =>
        Transaction.PostInterest(
            TenantId, AccountId, currency, decimalPlaces, accrued, December, withholding ?? Rwt175, BookedAt,
            source ?? TransactionSource.InterestRun(RunId));

    [Test]
    public void InterestRunSource_NeedsTheRunsId_AndKeepsIt()
    {
        Should.Throw<DomainException>(() => TransactionSource.InterestRun(Guid.Empty));
        var source = TransactionSource.InterestRun(RunId);
        source.Kind.ShouldBe(TransactionSourceKind.InterestRun);
        source.PublicId.ShouldBe(RunId);
        ((int)TransactionSourceKind.InterestRun).ShouldBe(6);
        ((int)CashLegRole.Tax).ShouldBe(2);
    }

    [Test]
    public void InterestRun_IsNotHandReversible_OnACustodialAccount()
    {
        CustodyRules.AllowsHandReverse(CustodyMode.Custodial, TransactionSourceKind.InterestRun).ShouldBeFalse();
    }

    [Test]
    public void PostInterest_IsAPrincipalLegOfTheGross_AndATaxLegOfTheTax_WithTheRunSourceAndMemo()
    {
        var posting = Post();

        posting.Type.ShouldBe(TransactionType.Interest);
        posting.TenantId.ShouldBe(TenantId);
        posting.BookedAt.ShouldBe(BookedAt);
        posting.Memo.ShouldBe("Interest");
        posting.Reference.ShouldBeNull();
        posting.Source.ShouldBe(TransactionSource.InterestRun(RunId));
        posting.CashLegs.Count.ShouldBe(2);
        var principal = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Principal);
        principal.AccountId.ShouldBe(AccountId);
        principal.Amount.ShouldBe(12.33m);
        principal.Currency.ShouldBe("USD");
        principal.TaxDetail.ShouldBeNull();
        var tax = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax);
        tax.AccountId.ShouldBe(AccountId);
        tax.Amount.ShouldBe(-2.16m);
        tax.Currency.ShouldBe("USD");
        tax.FeeDetail.ShouldBeNull();
    }

    [Test]
    public void PostInterest_TheTaxLegHasItsTaxDetail()
    {
        var posting = Post();

        var detail = posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).TaxDetail.ShouldNotBeNull();
        detail.TenantId.ShouldBe(TenantId);
        detail.Kind.ShouldBe(TaxKind.Rwt);
        detail.RatePercent.ShouldBe(17.5m);
        detail.BasisAmount.ShouldBe(12.33m);
        detail.TaxAmount.ShouldBe(2.16m);
        detail.Currency.ShouldBe("USD");
        posting.TaxDetails.ShouldBe([detail]);
    }

    [Test]
    public void PostInterest_HasTheInterestDetail()
    {
        var detail = Post().InterestDetail.ShouldNotBeNull();

        detail.TenantId.ShouldBe(TenantId);
        detail.PeriodFrom.ShouldBe(new DateOnly(2026, 12, 2));
        detail.PeriodTo.ShouldBe(new DateOnly(2026, 12, 31));
        detail.Days.ShouldBe(30);
        detail.AccruedAmount.ShouldBe(12.32876700m);
        detail.GrossAmount.ShouldBe(12.33m);
    }

    [TestCase(12.325, 2, 12.33, 17.5, 2.16)]
    [TestCase(0.005, 2, 0.01, 45, 0.00)]
    [TestCase(1234.5, 0, 1235, 17.5, 216)]
    [TestCase(1234.49999999, 0, 1234, 15, 185)]
    public void PostInterest_RoundsTheGrossAndTheTax_ToTheMinorUnit_AwayFromZero(
        decimal accrued, int decimalPlaces, decimal gross, decimal rate, decimal tax)
    {
        var posting = Post(accrued, new Withholding(TaxKind.Rwt, rate, WithholdingBasis.Elected), decimalPlaces == 0 ? "JPY" : "USD", decimalPlaces);

        posting.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(gross);
        posting.InterestDetail!.GrossAmount.ShouldBe(gross);
        posting.CashLegs.Where(leg => leg.Role == CashLegRole.Tax).Sum(leg => -leg.Amount).ShouldBe(tax);
    }

    [Test]
    public void PostInterest_ATaxOfZero_HasNoTaxLegAndNoTaxDetail()
    {
        var posting = Post(0.02m, new Withholding(TaxKind.Rwt, 10.5m, WithholdingBasis.Elected));

        posting.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        posting.TaxDetails.ShouldBeEmpty();
        posting.InterestDetail!.GrossAmount.ShouldBe(0.02m);
    }

    [Test]
    public void PostInterest_Nrwt_IsAnNrwtTaxDetail()
    {
        var posting = Post(withholding: new Withholding(TaxKind.Nrwt, 15m, WithholdingBasis.NonResident));

        var detail = posting.TaxDetails.ShouldHaveSingleItem();
        detail.Kind.ShouldBe(TaxKind.Nrwt);
        detail.TaxAmount.ShouldBe(1.85m);
    }

    [TestCase(0)]
    [TestCase(0.004)]
    [TestCase(-1)]
    public void PostInterest_AGrossOfZeroOrLess_Throws(decimal accrued)
    {
        Should.Throw<DomainException>(() => Post(accrued));
    }

    [Test]
    public void PostInterest_ASourceOtherThanAnInterestRun_Throws()
    {
        Should.Throw<DomainException>(() => Post(source: TransactionSource.Manual));
        Should.Throw<DomainException>(() => Post(source: TransactionSource.AccountClosure(RunId)));
    }

    [Test]
    public void Reverse_CopiesTheTaxLeg_ItsTaxDetail_AndTheInterestDetail()
    {
        var posting = Post();

        var reversal = posting.Reverse(TransactionSource.InterestRun(RunId), BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [AccountId] = 10.17m });

        reversal.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(-12.33m);
        var tax = reversal.CashLegs.Single(leg => leg.Role == CashLegRole.Tax);
        tax.Amount.ShouldBe(2.16m);
        var detail = tax.TaxDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(posting.TaxDetails.Single());
        (detail.Kind, detail.RatePercent, detail.BasisAmount, detail.TaxAmount, detail.Currency)
            .ShouldBe((TaxKind.Rwt, 17.5m, 12.33m, 2.16m, "USD"));
        reversal.TaxDetails.ShouldBe([detail]);
        var interest = reversal.InterestDetail.ShouldNotBeNull();
        interest.ShouldNotBeSameAs(posting.InterestDetail);
        (interest.PeriodFrom, interest.PeriodTo, interest.Days, interest.AccruedAmount, interest.GrossAmount)
            .ShouldBe((December.From, December.To, 30, 12.32876700m, 12.33m));
    }

    [Test]
    public void OtherFactories_HaveNoTaxLeg_AndNoInterestDetail()
    {
        var hand = Transaction.Post(TenantId, AccountId, TransactionType.Interest, 5m, "USD", BookedAt, null, null,
            TransactionSource.Manual, 0m, 1);
        var deposit = Transaction.PostBankDeposit(TenantId, AccountId, "USD", 100m, BookedAt,
            TransactionSource.BankFeed(RunId), new BankReceipt(new DateOnly(2026, 12, 1), null, null, null));

        foreach (var transaction in new[] { hand, deposit })
        {
            transaction.CashLegs.ShouldAllBe(leg => leg.Role != CashLegRole.Tax && leg.TaxDetail == null);
            transaction.TaxDetails.ShouldBeEmpty();
            transaction.InterestDetail.ShouldBeNull();
        }
    }
}
