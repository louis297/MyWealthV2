using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Interest;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R32–R42: the <c>Dividend</c> Transaction, its legs, details and Reversal.</summary>
public class DividendPostingTests
{
    private const int TenantId = 1;
    private const int AccountId = 7;
    private const int InstrumentId = 10;
    private static readonly DateOnly ExDate = new(2026, 11, 26);
    private static readonly DateOnly PaymentDate = new(2026, 11, 30);
    private static readonly DateTimeOffset At = new(2026, 11, 24, 4, 10, 31, TimeSpan.Zero);
    private static readonly TransactionSource Source = TransactionSource.CorporateAction(Guid.NewGuid());

    private static readonly DividendTax Rwt = new(TaxKind.Rwt, 33m);
    private static readonly DividendTax Nrwt = new(TaxKind.Nrwt, 30m);

    private static Transaction Post(
        DividendTerms terms,
        decimal quantity,
        DividendTax? withholding,
        string accountCurrency = "USD",
        string quoteCurrency = "USD",
        decimal? reinvestedQuantity = null,
        AppliedFxRate? fx = null,
        TransactionSource? source = null) =>
        Transaction.PostDividend(
            TenantId, AccountId, accountCurrency, 2, InstrumentId, "ACME.SIM", quoteCurrency, terms,
            ExDate, null, PaymentDate, quantity, terms.SourceAmounts(quantity, 2), withholding, reinvestedQuantity, fx,
            At, source ?? Source);

    private static decimal Leg(Transaction transaction, CashLegRole role, TaxKind? kind = null) =>
        transaction.CashLegs.Single(leg => leg.Role == role && (kind is null || leg.TaxDetail!.Kind == kind)).Amount;

    [Test]
    public void TheIr284Example_IsGross2000_Rwt526_Net1474()
    {
        var terms = DividendTerms.Create(2.00m, DividendTaxSource.NewZealand, 0.20m, null, null);
        var transaction = Post(terms, 1000m, Rwt);

        transaction.Type.ShouldBe(TransactionType.Dividend);
        Leg(transaction, CashLegRole.Principal).ShouldBe(2000.00m);
        Leg(transaction, CashLegRole.Tax, TaxKind.Rwt).ShouldBe(-526.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(1474.00m);
        var detail = transaction.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).TaxDetail!;
        (detail.Kind, detail.RatePercent, detail.BasisAmount, detail.TaxAmount, detail.Currency)
            .ShouldBe((TaxKind.Rwt, 33m, 2200.00m, 526.00m, "USD"));
        transaction.TaxDetails.Count.ShouldBe(1);
    }

    [Test]
    public void AFullyImputedDividend_ForANewZealandCustomer_HasRwt5_Net67()
    {
        var transaction = Post(DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null), 100m, Rwt);

        Leg(transaction, CashLegRole.Principal).ShouldBe(72.00m);
        Leg(transaction, CashLegRole.Tax, TaxKind.Rwt).ShouldBe(-5.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(67.00m);
        transaction.DividendDetail!.ImputationCredit.ShouldBe(28.00m);
    }

    [Test]
    public void AForeignDividend_ForANewZealandCustomer_HasForeignTax15_Rwt18_Net67()
    {
        var transaction = Post(DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, null), 100m, Rwt);

        Leg(transaction, CashLegRole.Principal).ShouldBe(100.00m);
        Leg(transaction, CashLegRole.Tax, TaxKind.ForeignWithholding).ShouldBe(-15.00m);
        Leg(transaction, CashLegRole.Tax, TaxKind.Rwt).ShouldBe(-18.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(67.00m);
        var foreign = transaction.CashLegs.Single(leg => leg.TaxDetail?.Kind == TaxKind.ForeignWithholding).TaxDetail!;
        (foreign.RatePercent, foreign.BasisAmount, foreign.TaxAmount).ShouldBe((15m, 100.00m, 15.00m));
        var rwt = transaction.CashLegs.Single(leg => leg.TaxDetail?.Kind == TaxKind.Rwt).TaxDetail!;
        (rwt.RatePercent, rwt.BasisAmount, rwt.TaxAmount).ShouldBe((33m, 100.00m, 18.00m));
    }

    [Test]
    public void ANewZealandDividend_ForAnOverseasCustomer_HasNrwt21_60_Net50_40()
    {
        var transaction = Post(DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null), 100m, Nrwt);

        Leg(transaction, CashLegRole.Tax, TaxKind.Nrwt).ShouldBe(-21.60m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(50.40m);
        var detail = transaction.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).TaxDetail!;
        (detail.RatePercent, detail.BasisAmount).ShouldBe((30m, 72.00m));
    }

    [Test]
    public void AForeignDividend_ForAnOverseasCustomer_HasOnlyTheForeignTax_Net85()
    {
        var transaction = Post(DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, null), 100m, null);

        transaction.CashLegs.Count.ShouldBe(2);
        Leg(transaction, CashLegRole.Tax, TaxKind.ForeignWithholding).ShouldBe(-15.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(85.00m);
    }

    [Test]
    public void ADividend_HasSourceCorporateAction_TheMemo_NoReference_AndTheDetail_AndNoSecurityLeg()
    {
        var transaction = Post(DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null), 100m, Rwt);

        transaction.Source.ShouldBe(Source);
        transaction.Memo.ShouldBe("Dividend ACME.SIM 0.72 per share");
        transaction.Reference.ShouldBeNull();
        transaction.BookedAt.ShouldBe(At);
        transaction.SecurityLegs.ShouldBeEmpty();
        transaction.FxDetail.ShouldBeNull();
        transaction.CashLegs.ShouldAllBe(leg => leg.AccountId == AccountId && leg.Currency == "USD");
        var detail = transaction.DividendDetail!;
        (detail.InstrumentId, detail.Quantity, detail.AmountPerShare, detail.Currency, detail.TaxSource)
            .ShouldBe((InstrumentId, 100m, 0.72m, "USD", DividendTaxSource.NewZealand));
        (detail.ExDate, detail.RecordDate, detail.PaymentDate).ShouldBe((ExDate, (DateOnly?)null, (DateOnly?)PaymentDate));
        detail.ReinvestedQuantity.ShouldBeNull();
        detail.ReinvestmentPrice.ShouldBeNull();
    }

    [Test]
    public void TheMemo_ShowsAtLeastTwoDecimals() =>
        Post(DividendTerms.Create(1m, DividendTaxSource.Foreign, null, 15m, null), 100m, null)
            .Memo.ShouldBe("Dividend ACME.SIM 1.00 per share");

    [Test]
    public void AReinvestment_AddsTheReinvestmentLeg_AndASecurityLegWithACostEqualToIt()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);
        var transaction = Post(terms, 100m, Rwt, reinvestedQuantity: 2.68m);

        Leg(transaction, CashLegRole.Reinvestment).ShouldBe(-67.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(0m);
        var security = transaction.SecurityLegs.ShouldHaveSingleItem();
        (security.AccountId, security.InstrumentId, security.Quantity, security.CostAmount, security.CostCurrency)
            .ShouldBe((AccountId, InstrumentId, 2.68m, 67.00m, "USD"));
        (transaction.DividendDetail!.ReinvestedQuantity, transaction.DividendDetail.ReinvestmentPrice)
            .ShouldBe(((decimal?)2.68m, (decimal?)25m));
    }

    [Test]
    public void TheReinvestedAmount_IsCappedAtTheNet()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);
        var transaction = Post(terms, 100m, Rwt, reinvestedQuantity: 2.70m);

        Leg(transaction, CashLegRole.Reinvestment).ShouldBe(-67.00m);
        transaction.SecurityLegs.Single().CostAmount.ShouldBe(67.00m);
    }

    [Test]
    public void AReinvestment_LeavesTheRestOfTheNetInCash()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);
        var transaction = Post(terms, 100m, Rwt, reinvestedQuantity: 2m);

        Leg(transaction, CashLegRole.Reinvestment).ShouldBe(-50.00m);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(17.00m);
    }

    [Test]
    public void AnAccountInAnotherCurrency_IsConvertedAtTheRate_WithAnFxDetail()
    {
        var rate = AppliedFxRate.Create("USD", "NZD", 1.7m, new DateOnly(2026, 11, 24), "Frankfurter");
        var transaction = Post(
            DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, null), 100m, Rwt, accountCurrency: "NZD", fx: rate);

        Leg(transaction, CashLegRole.Principal).ShouldBe(170.00m);
        Leg(transaction, CashLegRole.Tax, TaxKind.ForeignWithholding).ShouldBe(-25.50m);
        // 0.33 × 170.00 − 25.50 = 30.60.
        Leg(transaction, CashLegRole.Tax, TaxKind.Rwt).ShouldBe(-30.60m);
        transaction.CashLegs.ShouldAllBe(leg => leg.Currency == "NZD");
        transaction.FxDetail.ShouldNotBeNull();
        (transaction.DividendDetail!.Currency, transaction.DividendDetail.AmountPerShare).ShouldBe(("USD", 1.00m));
    }

    [Test]
    public void AnAccountInAnotherCurrency_WithoutARate_Throws() =>
        Should.Throw<DomainException>(() =>
            Post(DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, null), 100m, Rwt, accountCurrency: "NZD"));

    [Test]
    public void AReinvestment_AcrossCurrencies_Throws()
    {
        var rate = AppliedFxRate.Create("USD", "NZD", 1.7m, new DateOnly(2026, 11, 24), "Frankfurter");
        Should.Throw<DomainException>(() => Post(
            DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m), 100m, Rwt,
            accountCurrency: "NZD", reinvestedQuantity: 2.68m, fx: rate));
    }

    [Test]
    public void AReinvestment_WithoutAPlan_Throws() =>
        Should.Throw<DomainException>(() =>
            Post(DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, null), 100m, Rwt, reinvestedQuantity: 2m));

    [Test]
    public void ASourceOtherThanCorporateAction_Throws() =>
        Should.Throw<DomainException>(() => Post(
            DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null), 100m, Rwt,
            source: TransactionSource.InterestRun(Guid.NewGuid())));

    [Test]
    public void AGrossOfZero_Throws() =>
        Should.Throw<DomainException>(() =>
            Post(DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, null, null), 0.001m, Rwt));

    [Test]
    public void AForeignWithholdingTaxLeg_OnInterest_Throws() =>
        Should.Throw<DomainException>(() => Transaction.PostInterest(
            TenantId, AccountId, "NZD", 2, 10m, new InterestPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 30),
            new Withholding(TaxKind.ForeignWithholding, 15m, WithholdingBasis.Default), At,
            TransactionSource.InterestRun(Guid.NewGuid())));

    [Test]
    public void Reverse_CopiesTheDividendDetail_TheTaxDetails_AndTheRoles()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m);
        var transaction = Post(terms, 100m, Rwt, reinvestedQuantity: 2.68m);

        var reversal = transaction.Reverse(
            TransactionSource.CorporateAction(Guid.NewGuid()), At.AddMinutes(1),
            new Dictionary<int, decimal> { [AccountId] = 0m },
            new Dictionary<(int, int), decimal> { [(AccountId, InstrumentId)] = 102.68m });

        reversal.CashLegs.Select(leg => (leg.Role, leg.Amount)).ShouldBe(
            transaction.CashLegs.Select(leg => (leg.Role, -leg.Amount)), ignoreOrder: true);
        reversal.TaxDetails.Select(detail => detail.Kind).ShouldBe([TaxKind.ForeignWithholding, TaxKind.Rwt], ignoreOrder: true);
        reversal.DividendDetail.ShouldNotBeNull();
        reversal.DividendDetail.ShouldNotBeSameAs(transaction.DividendDetail);
        (reversal.DividendDetail!.Quantity, reversal.DividendDetail.ReinvestedQuantity, reversal.DividendDetail.ReinvestmentPrice)
            .ShouldBe((100m, (decimal?)2.68m, (decimal?)25m));
        reversal.SecurityLegs.Single().Quantity.ShouldBe(-2.68m);
    }

    [Test]
    public void OtherTransactions_HaveNoDividendDetail() =>
        Transaction.PostInterest(
                TenantId, AccountId, "NZD", 2, 10m, new InterestPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 30),
                WithholdingRates.For(TaxResidency.NewZealand, true, null), At, TransactionSource.InterestRun(Guid.NewGuid()))
            .DividendDetail.ShouldBeNull();
}
