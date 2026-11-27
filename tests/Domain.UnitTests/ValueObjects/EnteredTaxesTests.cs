using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

/// <summary>External income R5–R7 and R12: the shape of the entered taxes, the kinds and the pairs with the tax
/// source.</summary>
public class EnteredTaxesTests
{
    private static EnteredTax Tax(TaxKind kind = TaxKind.Rwt, decimal rate = 33m, decimal amount = 4.07m) =>
        new(kind, rate, amount);

    [Test]
    public void NoTax_AndUpToThreeOfDifferentKinds_AreAccepted()
    {
        EnteredTaxes.RefusalOf([]).ShouldBeNull();
        EnteredTaxes.RefusalOf([Tax()]).ShouldBeNull();
        EnteredTaxes.RefusalOf([Tax(TaxKind.ForeignWithholding, 15m, 15m), Tax(TaxKind.Rwt, 33m, 18m)]).ShouldBeNull();
        EnteredTaxes.RefusalOf([Tax(TaxKind.Nrwt, 15m, 1m), Tax(TaxKind.ForeignWithholding, 10m, 5m)]).ShouldBeNull();
    }

    [Test]
    public void MoreThanThree_IsRefusedOnTaxes() =>
        EnteredTaxes.RefusalOf(
                [Tax(TaxKind.Rwt), Tax(TaxKind.ForeignWithholding), Tax(TaxKind.Nrwt), Tax(TaxKind.ForeignWithholding)])
            .ShouldBe(("Taxes", EnteredTaxes.TooManyMessage));

    [Test]
    public void AnUndefinedKind_IsRefusedAtTheRow() =>
        EnteredTaxes.RefusalOf([Tax(), Tax((TaxKind)7)]).ShouldBe(("Taxes[1].Kind", EnteredTaxes.KindMessage));

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(100.01)]
    [TestCase(33.333)]
    public void ARateOutsideTheShape_IsRefusedAtTheRow(decimal rate) =>
        EnteredTaxes.RefusalOf([Tax(rate: rate)]).ShouldBe(("Taxes[0].RatePercent", EnteredTaxes.RateMessage));

    [TestCase(100)]
    [TestCase(0.01)]
    [TestCase(10.5)]
    public void ARateAbove0UpTo100WithTwoDecimals_IsAccepted(decimal rate) =>
        EnteredTaxes.RefusalOf([Tax(rate: rate)]).ShouldBeNull();

    [TestCase(0)]
    [TestCase(-4.07)]
    public void AnAmountNotAbove0_IsRefusedAtTheRow(decimal amount) =>
        EnteredTaxes.RefusalOf([Tax(TaxKind.ForeignWithholding), Tax(amount: amount)])
            .ShouldBe(("Taxes[1].Amount", EnteredTaxes.AmountMessage));

    [Test]
    public void AKindTwice_IsRefusedOnTaxes() =>
        EnteredTaxes.RefusalOf([Tax(TaxKind.Rwt, 33m, 1m), Tax(TaxKind.Rwt, 10.5m, 2m)])
            .ShouldBe(("Taxes", EnteredTaxes.KindTwiceMessage));

    [Test]
    public void RwtWithNrwt_IsRefusedOnTaxes() =>
        EnteredTaxes.RefusalOf([Tax(TaxKind.Rwt), Tax(TaxKind.Nrwt, 15m, 1m)])
            .ShouldBe(("Taxes", EnteredTaxes.RwtWithNrwtMessage));

    [TestCase(DividendTaxSource.NewZealand, TaxKind.Rwt)]
    [TestCase(DividendTaxSource.NewZealand, TaxKind.Nrwt)]
    [TestCase(DividendTaxSource.Foreign, TaxKind.Rwt)]
    [TestCase(DividendTaxSource.Foreign, TaxKind.ForeignWithholding)]
    public void AKindThatGoesWithTheTaxSource_IsAccepted(DividendTaxSource source, TaxKind kind) =>
        EnteredTaxes.RefusalOf([Tax(kind)], source).ShouldBeNull();

    [Test]
    public void ForeignTaxOnANewZealandCompanysDividend_IsRefusedAtTheRow() =>
        EnteredTaxes.RefusalOf([Tax(TaxKind.Rwt), Tax(TaxKind.ForeignWithholding, 15m, 1m)], DividendTaxSource.NewZealand)
            .ShouldBe(("Taxes[1].Kind", EnteredTaxes.ForeignOnlyMessage));

    [Test]
    public void NrwtOnAForeignCompanysDividend_IsRefusedAtTheRow() =>
        EnteredTaxes.RefusalOf([Tax(TaxKind.Nrwt, 30m, 1m)], DividendTaxSource.Foreign)
            .ShouldBe(("Taxes[0].Kind", EnteredTaxes.NrwtOnlyMessage));

    [TestCase(2000, false)]
    [TestCase(15.01, false)]
    [TestCase(15.00, false)]
    [TestCase(14.99, true)]
    public void AboveGross_ComparesTheSumWithTheGross(decimal gross, bool above) =>
        EnteredTaxes.AboveGross([10.00m, 5.00m], gross).ShouldBe(above);
}
