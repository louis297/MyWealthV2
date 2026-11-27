using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>External income R7, R8, R14, R16, R17: a dividend entered by hand on an External account (§1 examples
/// A–D).</summary>
public class EnteredDividendTests
{
    private const int TenantId = 1;
    private const int AccountId = 7;
    private const int InstrumentId = 10;
    private static readonly DateTimeOffset At = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private static EnteredTax Tax(TaxKind kind, decimal rate, decimal amount) => new(kind, rate, amount);

    private static Transaction Post(
        decimal quantity,
        decimal grossAmount,
        DividendTaxSource taxSource,
        IReadOnlyList<EnteredTax>? taxes = null,
        decimal imputationCredit = 0m,
        string accountCurrency = "NZD",
        string quoteCurrency = "NZD",
        decimal? amountPerShare = null,
        AppliedFxRate? fx = null,
        DateOnly? exDate = null,
        DateOnly? recordDate = null,
        DateOnly? paymentDate = null,
        string? memo = null,
        string? reference = null,
        TransactionSource? source = null) =>
        Transaction.PostEnteredDividend(
            TenantId, AccountId, accountCurrency, 2, InstrumentId, quoteCurrency, quantity, amountPerShare, grossAmount,
            taxSource, imputationCredit, taxes ?? [], fx, exDate, recordDate, paymentDate, At, memo, reference,
            source ?? TransactionSource.Manual);

    private static AppliedFxRate Caller(decimal rate, string from = "USD", string to = "NZD") =>
        AppliedFxRate.Create(from, to, rate, DateOnly.FromDateTime(At.UtcDateTime), AppliedFxRate.CallerSource);

    private static IEnumerable<(CashLegRole Role, decimal Amount, TaxKind? Kind)> Legs(Transaction transaction) =>
        transaction.CashLegs.Select(leg => (leg.Role, leg.Amount, leg.TaxDetail?.Kind));

    private static (TaxKind, decimal, decimal, decimal, string) Detail(Transaction transaction, TaxKind kind)
    {
        var detail = transaction.CashLegs.Single(leg => leg.TaxDetail?.Kind == kind).TaxDetail!;
        return (detail.Kind, detail.RatePercent, detail.BasisAmount, detail.TaxAmount, detail.Currency);
    }

    [Test]
    public void ExampleA_ANewZealandCompany_HasTheRwtLeg_WithTheBasisOfGrossPlusCredit()
    {
        var transaction = Post(1000m, 2000.00m, DividendTaxSource.NewZealand, [Tax(TaxKind.Rwt, 33m, 526.00m)], 200.00m);

        transaction.Type.ShouldBe(TransactionType.Dividend);
        Legs(transaction).ShouldBe(
            [(CashLegRole.Principal, 2000.00m, null), (CashLegRole.Tax, -526.00m, TaxKind.Rwt)], ignoreOrder: true);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(1474.00m);
        transaction.CashLegs.ShouldAllBe(leg => leg.AccountId == AccountId && leg.Currency == "NZD");
        Detail(transaction, TaxKind.Rwt).ShouldBe((TaxKind.Rwt, 33m, 2200.00m, 526.00m, "NZD"));
        transaction.TaxDetails.Count.ShouldBe(1);
        var dividend = transaction.DividendDetail.ShouldNotBeNull();
        (dividend.InstrumentId, dividend.Quantity, dividend.AmountPerShare, dividend.Currency, dividend.TaxSource,
                dividend.ImputationCredit, dividend.ReinvestedQuantity, dividend.ReinvestmentPrice, dividend.ExDate)
            .ShouldBe((InstrumentId, 1000m, (decimal?)null, "NZD", DividendTaxSource.NewZealand, 200.00m,
                (decimal?)null, (decimal?)null, (DateOnly?)null));
        transaction.FxDetail.ShouldBeNull();
    }

    [Test]
    public void ExampleB_AForeignCompany_HasOneTaxLegPerTax_EachOnTheGross()
    {
        var transaction = Post(
            100m, 100.00m, DividendTaxSource.Foreign,
            [Tax(TaxKind.ForeignWithholding, 15m, 15.00m), Tax(TaxKind.Rwt, 33m, 18.00m)],
            accountCurrency: "USD", quoteCurrency: "USD");

        Legs(transaction).ShouldBe(
        [
            (CashLegRole.Principal, 100.00m, null),
            (CashLegRole.Tax, -15.00m, TaxKind.ForeignWithholding),
            (CashLegRole.Tax, -18.00m, TaxKind.Rwt)
        ], ignoreOrder: true);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(67.00m);
        Detail(transaction, TaxKind.ForeignWithholding).ShouldBe((TaxKind.ForeignWithholding, 15m, 100.00m, 15.00m, "USD"));
        Detail(transaction, TaxKind.Rwt).ShouldBe((TaxKind.Rwt, 33m, 100.00m, 18.00m, "USD"));
        transaction.TaxDetails.Count.ShouldBe(2);
    }

    [Test]
    public void ExampleC_AnotherCurrency_ConvertsEachAmountAtTheCallersRate_WithAnFxDetail()
    {
        var transaction = Post(
            10m, 2.60m, DividendTaxSource.Foreign, [Tax(TaxKind.ForeignWithholding, 15m, 0.39m)],
            quoteCurrency: "USD", amountPerShare: 0.26m, fx: Caller(1.69147m),
            exDate: new DateOnly(2026, 11, 9), recordDate: new DateOnly(2026, 11, 10),
            paymentDate: new DateOnly(2026, 11, 13), memo: " Quarterly dividend ", reference: "HATCH-2026-11");

        // 2.60 × 1.69147 = 4.397822 → 4.40; 0.39 × 1.69147 = 0.6596733 → 0.66.
        Legs(transaction).ShouldBe(
            [(CashLegRole.Principal, 4.40m, null), (CashLegRole.Tax, -0.66m, TaxKind.ForeignWithholding)], ignoreOrder: true);
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(3.74m);
        transaction.CashLegs.ShouldAllBe(leg => leg.Currency == "NZD");
        Detail(transaction, TaxKind.ForeignWithholding).ShouldBe((TaxKind.ForeignWithholding, 15m, 4.40m, 0.66m, "NZD"));
        var fx = transaction.FxDetail.ShouldNotBeNull();
        (fx.BaseCurrency, fx.QuoteCurrency, fx.Rate, fx.RateAsOfDate, fx.RateSource)
            .ShouldBe(("USD", "NZD", 1.69147m, new DateOnly(2026, 11, 20), "Caller"));
        var dividend = transaction.DividendDetail.ShouldNotBeNull();
        (dividend.AmountPerShare, dividend.Currency, dividend.ImputationCredit, dividend.ExDate, dividend.RecordDate,
                dividend.PaymentDate)
            .ShouldBe(((decimal?)0.26m, "USD", 0m, (DateOnly?)new DateOnly(2026, 11, 9),
                (DateOnly?)new DateOnly(2026, 11, 10), (DateOnly?)new DateOnly(2026, 11, 13)));
        (transaction.Memo, transaction.Reference).ShouldBe(("Quarterly dividend", "HATCH-2026-11"));
    }

    [Test]
    public void ExampleD_NoTax_HasOnlyThePrincipalLeg()
    {
        var transaction = Post(50m, 12.50m, DividendTaxSource.NewZealand);

        Legs(transaction).ShouldBe([(CashLegRole.Principal, 12.50m, null)]);
        transaction.TaxDetails.ShouldBeEmpty();
        transaction.DividendDetail.ShouldNotBeNull().ImputationCredit.ShouldBe(0m);
    }

    [Test]
    public void TheImputationCredit_IsConvertedLikeTheGross_AndInTheRwtBasis()
    {
        var transaction = Post(
            100m, 10.00m, DividendTaxSource.NewZealand, [Tax(TaxKind.Rwt, 33m, 1.00m)], 2.00m,
            quoteCurrency: "USD", fx: Caller(1.5m));

        transaction.DividendDetail!.ImputationCredit.ShouldBe(3.00m);
        Detail(transaction, TaxKind.Rwt).ShouldBe((TaxKind.Rwt, 33m, 18.00m, 1.50m, "NZD"));
    }

    [Test]
    public void IsManual_WithNoSecurityLeg_AndRaisesTransactionPosted()
    {
        var transaction = Post(50m, 12.50m, DividendTaxSource.NewZealand);

        (transaction.SourceKind, transaction.SourcePublicId).ShouldBe((TransactionSourceKind.Manual, (Guid?)null));
        transaction.SecurityLegs.ShouldBeEmpty();
        transaction.CashLegs.ShouldNotContain(leg => leg.Role == CashLegRole.Reinvestment);
        transaction.BookedAt.ShouldBe(At);
        transaction.DomainEvents.OfType<TransactionPosted>().Count().ShouldBe(1);
    }

    [Test]
    public void ATaxThatConvertsTo0_BooksNoLeg()
    {
        var transaction = Post(
            100m, 10.00m, DividendTaxSource.Foreign, [Tax(TaxKind.ForeignWithholding, 15m, 0.01m)],
            quoteCurrency: "USD", fx: Caller(0.4m));

        Legs(transaction).ShouldBe([(CashLegRole.Principal, 4.00m, null)]);
        transaction.TaxDetails.ShouldBeEmpty();
    }

    [Test]
    public void TaxesEqualToTheGross_AreAccepted() =>
        Post(10m, 1.00m, DividendTaxSource.Foreign,
                [Tax(TaxKind.ForeignWithholding, 60m, 0.60m), Tax(TaxKind.Rwt, 40m, 0.40m)], accountCurrency: "USD",
                quoteCurrency: "USD")
            .CashLegs.Sum(leg => leg.Amount).ShouldBe(0m);

    [Test]
    public void TaxesAboveTheGross_Throw() =>
        Should.Throw<DomainException>(() => Post(
                10m, 1.00m, DividendTaxSource.Foreign,
                [Tax(TaxKind.ForeignWithholding, 60m, 0.60m), Tax(TaxKind.Rwt, 45m, 0.45m)]))
            .Message.ShouldBe(EnteredTaxes.AboveGrossMessage);

    [Test]
    public void TaxesAboveTheGrossOnlyAfterTheConversion_Throw() =>
        // 0.01 × 1.5 = 0.015 → 0.02 each; the gross 0.02 × 1.5 = 0.03.
        Should.Throw<DomainException>(() => Post(
                10m, 0.02m, DividendTaxSource.Foreign,
                [Tax(TaxKind.ForeignWithholding, 50m, 0.01m), Tax(TaxKind.Rwt, 50m, 0.01m)], quoteCurrency: "USD",
                fx: Caller(1.5m)))
            .Message.ShouldBe(EnteredTaxes.AboveGrossMessage);

    [Test]
    public void AGrossThatConvertsTo0_Throws() =>
        Should.Throw<DomainException>(() => Post(
            10m, 0.01m, DividendTaxSource.Foreign, quoteCurrency: "USD", fx: Caller(0.4m)));

    [TestCase(0)]
    [TestCase(-1)]
    public void AGrossOrAQuantityNotAbove0_Throws(decimal value)
    {
        Should.Throw<DomainException>(() => Post(10m, value, DividendTaxSource.NewZealand));
        Should.Throw<DomainException>(() => Post(value, 1.00m, DividendTaxSource.NewZealand));
    }

    [Test]
    public void ACreditWithAForeignCompany_OrAboveTheGross_Throws()
    {
        Should.Throw<DomainException>(() => Post(10m, 1.00m, DividendTaxSource.Foreign, imputationCredit: 0.10m));
        Should.Throw<DomainException>(() => Post(10m, 1.00m, DividendTaxSource.NewZealand, imputationCredit: 1.01m));
    }

    [Test]
    public void TaxesThatBreakTheirRules_Throw()
    {
        Should.Throw<DomainException>(() => Post(
            10m, 10.00m, DividendTaxSource.NewZealand, [Tax(TaxKind.Rwt, 33m, 1m), Tax(TaxKind.Nrwt, 30m, 1m)]));
        Should.Throw<DomainException>(() => Post(
            10m, 10.00m, DividendTaxSource.NewZealand, [Tax(TaxKind.ForeignWithholding, 15m, 1m)]));
        Should.Throw<DomainException>(() => Post(
            10m, 10.00m, DividendTaxSource.Foreign, [Tax(TaxKind.Nrwt, 30m, 1m)]));
    }

    [Test]
    public void AnotherCurrency_WithoutARate_OrOneCurrencyWithARate_Throws()
    {
        Should.Throw<DomainException>(() => Post(10m, 1.00m, DividendTaxSource.Foreign, quoteCurrency: "USD"));
        Should.Throw<DomainException>(() => Post(10m, 1.00m, DividendTaxSource.Foreign, fx: Caller(1m, "NZD", "USD")));
    }

    [Test]
    public void ASourceOtherThanManual_Throws() =>
        Should.Throw<DomainException>(() => Post(
            50m, 12.50m, DividendTaxSource.NewZealand, source: TransactionSource.CorporateAction(Guid.NewGuid())));

    [Test]
    public void Reverse_CopiesTheLegs_TheTaxDetails_TheDividendDetail_AndTheFxDetail()
    {
        var transaction = Post(
            10m, 2.60m, DividendTaxSource.Foreign, [Tax(TaxKind.ForeignWithholding, 15m, 0.39m)],
            quoteCurrency: "USD", fx: Caller(1.69147m));

        var reversal = transaction.Reverse(
            TransactionSource.Manual, At.AddMinutes(1), new Dictionary<int, decimal> { [AccountId] = 3.74m });

        reversal.CashLegs.Select(leg => (leg.Role, leg.Amount)).ShouldBe(
            transaction.CashLegs.Select(leg => (leg.Role, -leg.Amount)), ignoreOrder: true);
        reversal.TaxDetails.Select(detail => (detail.Kind, detail.RatePercent, detail.BasisAmount, detail.TaxAmount))
            .ShouldBe([(TaxKind.ForeignWithholding, 15m, 4.40m, 0.66m)]);
        var dividend = reversal.DividendDetail.ShouldNotBeNull();
        dividend.ShouldNotBeSameAs(transaction.DividendDetail);
        (dividend.Quantity, dividend.AmountPerShare, dividend.ExDate).ShouldBe((10m, (decimal?)null, (DateOnly?)null));
        reversal.FxDetail.ShouldNotBeNull().Rate.ShouldBe(1.69147m);
    }

    [Test]
    public void Reverse_WhenTheNetWasSpent_Throws()
    {
        var transaction = Post(50m, 12.50m, DividendTaxSource.NewZealand);

        Should.Throw<DomainException>(() => transaction.Reverse(
            TransactionSource.Manual, At.AddMinutes(1), new Dictionary<int, decimal> { [AccountId] = 12.49m }));
    }
}
