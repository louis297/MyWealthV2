using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Interest;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R19, R33 and R34: the New Zealand tax on a dividend and the participating quantity.</summary>
public class DividendWithholdingTests
{
    [Test]
    public void Rates_AreTheDividendConstants()
    {
        WithholdingRates.DividendRwt.ShouldBe(33m);
        WithholdingRates.DividendNrwt.ShouldBe(30m);
    }

    [TestCase(TaxResidency.NewZealand, DividendTaxSource.NewZealand, TaxKind.Rwt, 33)]
    [TestCase(TaxResidency.NewZealand, DividendTaxSource.Foreign, TaxKind.Rwt, 33)]
    [TestCase(TaxResidency.Overseas, DividendTaxSource.NewZealand, TaxKind.Nrwt, 30)]
    public void For_GivesTheKindAndRateOfR33(
        TaxResidency residency, DividendTaxSource source, TaxKind kind, decimal rate) =>
        DividendWithholding.For(residency, source).ShouldBe(new DividendTax(kind, rate));

    [Test]
    public void For_AnOverseasCustomerAndAForeignCompany_HasNoNewZealandTax() =>
        DividendWithholding.For(TaxResidency.Overseas, DividendTaxSource.Foreign).ShouldBeNull();

    // §1 worked examples: residency, gross, credit, foreign tax → New Zealand tax.
    [TestCase(TaxResidency.NewZealand, DividendTaxSource.NewZealand, 2000.00, 200.00, 0, 526.00)]
    [TestCase(TaxResidency.NewZealand, DividendTaxSource.NewZealand, 72.00, 28.00, 0, 5.00)]
    [TestCase(TaxResidency.NewZealand, DividendTaxSource.Foreign, 100.00, 0, 15.00, 18.00)]
    [TestCase(TaxResidency.Overseas, DividendTaxSource.NewZealand, 72.00, 28.00, 0, 21.60)]
    public void Amount_OfTheWorkedExamples(
        TaxResidency residency, DividendTaxSource source, decimal gross, decimal credit, decimal foreign, decimal tax)
    {
        var withholding = DividendWithholding.For(residency, source)!;

        DividendWithholding.Amount(withholding, gross, credit, foreign, 2).ShouldBe(tax);
    }

    [Test]
    public void Amount_AForeignTaxAbove33Percent_IsNoRwt() =>
        DividendWithholding.Amount(new DividendTax(TaxKind.Rwt, 33m), 100m, 0m, 35m, 2).ShouldBe(0m);

    [Test]
    public void Amount_IsNeverBelowZero() =>
        DividendWithholding.Amount(new DividendTax(TaxKind.Rwt, 33m), 10m, 10m, 0m, 2).ShouldBe(0m);

    [Test]
    public void Amount_RoundsAwayFromZeroAtTheMidpoint() =>
        // 0.33 × 1.50 = 0.495 → 0.50.
        DividendWithholding.Amount(new DividendTax(TaxKind.Rwt, 33m), 1.50m, 0m, 0m, 2).ShouldBe(0.50m);

    [TestCase(TaxKind.Rwt, 33, 100.00, 28.00, 128.00)]
    [TestCase(TaxKind.Nrwt, 30, 72.00, 28.00, 72.00)]
    public void Basis_IsTheGrossPlusTheCreditForRwt_AndTheGrossForNrwt(
        TaxKind kind, decimal rate, decimal gross, decimal credit, decimal basis) =>
        DividendWithholding.Basis(new DividendTax(kind, rate), gross, credit).ShouldBe(basis);

    [Test]
    public void PerShare_IsTheUnroundedTaxOfOneShare()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 15m, 25m);

        DividendWithholding.PerShare(new DividendTax(TaxKind.Rwt, 33m), terms).ShouldBe(0.18m);
        DividendWithholding.PerShare(null, terms).ShouldBe(0m);
    }

    [Test]
    public void ParticipatingQuantity_OfThePlanExample_Is78_82352941()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 15m, 25m);

        DividendWithholding.ParticipatingQuantity(100m, terms, new DividendTax(TaxKind.Rwt, 33m))
            .ShouldBe(78.82352941m);
    }

    [Test]
    public void ParticipatingQuantity_WithNoNewZealandTax_IsTheWholeQuantity()
    {
        var terms = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 15m, 25m);

        DividendWithholding.ParticipatingQuantity(100m, terms, null).ShouldBe(100m);
    }

    [Test]
    public void ParticipatingQuantity_WhenNoRwtIsDue_IsTheWholeQuantity()
    {
        // A foreign tax above 33 %, and a credit that covers the RWT, each leave no RWT per share.
        var foreign = DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 40m, 25m);
        DividendWithholding.ParticipatingQuantity(100m, foreign, new DividendTax(TaxKind.Rwt, 33m)).ShouldBe(100m);
        var imputed = DividendTerms.Create(1.00m, DividendTaxSource.NewZealand, 1.00m, 0m, 25m);
        DividendWithholding.ParticipatingQuantity(100m, imputed, new DividendTax(TaxKind.Rwt, 33m)).ShouldBe(100m);
    }

    [Test]
    public void ParticipatingQuantity_WithNrwt_LeavesThirtyPercentInCash()
    {
        var terms = DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, 0m, 25m);

        DividendWithholding.ParticipatingQuantity(100m, terms, new DividendTax(TaxKind.Nrwt, 30m)).ShouldBe(70m);
    }
}
