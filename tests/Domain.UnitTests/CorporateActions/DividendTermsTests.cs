using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R3 and R14: the terms of a dividend, their refusals and the source amounts of an entitlement.</summary>
public class DividendTermsTests
{
    private static (string Field, string Message)? Refusal(
        decimal? amount = 0.72m,
        DividendTaxSource? source = DividendTaxSource.NewZealand,
        decimal? credit = 0m,
        decimal? foreignRate = 0m,
        decimal? price = null) =>
        DividendTerms.RefusalOf(amount, source, credit, foreignRate, price);

    [Test]
    public void ValidTerms_HaveNoRefusal()
    {
        Refusal().ShouldBeNull();
        Refusal(amount: 1_000_000m, credit: 1_000_000m).ShouldBeNull();
        Refusal(amount: 0.00000001m).ShouldBeNull();
        Refusal(source: DividendTaxSource.Foreign, foreignRate: 99.99m, price: 25.12345678m).ShouldBeNull();
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(1_000_000.00000001)]
    [TestCase(0.000000001)]
    public void AmountPerShare_OutsideTheRule_IsRefusedAtItsField(decimal? amount) =>
        Refusal(amount: amount)!.Value.Field.ShouldBe("AmountPerShare");

    [Test]
    public void TaxSource_Missing_IsRefusedAtItsField() =>
        Refusal(source: null)!.Value.Field.ShouldBe("TaxSource");

    [Test]
    public void TaxSource_Unknown_IsRefusedAtItsField() =>
        Refusal(source: (DividendTaxSource)7)!.Value.Field.ShouldBe("TaxSource");

    [TestCase(-0.01, DividendTaxSource.NewZealand)]
    [TestCase(0.000000001, DividendTaxSource.NewZealand)]
    [TestCase(0.73, DividendTaxSource.NewZealand)]
    [TestCase(0.1, DividendTaxSource.Foreign)]
    public void ImputationCredit_OutsideTheRule_IsRefusedAtItsField(decimal credit, DividendTaxSource source) =>
        Refusal(source: source, credit: credit)!.Value.Field.ShouldBe("ImputationCreditPerShare");

    [Test]
    public void ImputationCredit_EqualToTheAmount_IsAllowed() =>
        Refusal(credit: 0.72m).ShouldBeNull();

    [TestCase(-1, DividendTaxSource.Foreign)]
    [TestCase(100, DividendTaxSource.Foreign)]
    [TestCase(15.001, DividendTaxSource.Foreign)]
    [TestCase(15, DividendTaxSource.NewZealand)]
    public void ForeignTaxRate_OutsideTheRule_IsRefusedAtItsField(decimal rate, DividendTaxSource source) =>
        Refusal(source: source, foreignRate: rate)!.Value.Field.ShouldBe("ForeignTaxRatePercent");

    [TestCase(0)]
    [TestCase(-25)]
    [TestCase(25.000000001)]
    public void ReinvestmentPrice_OutsideTheRule_IsRefusedAtItsField(decimal price) =>
        Refusal(price: price)!.Value.Field.ShouldBe("ReinvestmentPrice");

    [Test]
    public void Create_WithARefusal_Throws() =>
        Should.Throw<DomainException>(() => DividendTerms.Create(0m, DividendTaxSource.NewZealand, 0m, 0m, null));

    [Test]
    public void Create_DefaultsTheCreditAndTheForeignRateToZero()
    {
        var terms = DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, null, null, null);
        terms.ImputationCreditPerShare.ShouldBe(0m);
        terms.ForeignTaxRatePercent.ShouldBe(0m);
        terms.ReinvestmentPrice.ShouldBeNull();
    }

    [Test]
    public void NetPerShare_IsTheAmountLessTheForeignTax() =>
        DividendTerms.Create(1.00m, DividendTaxSource.Foreign, 0m, 15m, 25m).NetPerShare.ShouldBe(0.85m);

    // §1 worked examples: shares, per share, credit, foreign rate → gross, credit, foreign tax.
    [TestCase(1000, 2.00, 0.20, 0, 2000.00, 200.00, 0)]
    [TestCase(100, 0.72, 0.28, 0, 72.00, 28.00, 0)]
    [TestCase(100, 1.00, 0, 15, 100.00, 0, 15.00)]
    public void SourceAmounts_OfTheWorkedExamples(
        decimal quantity, decimal amount, decimal credit, decimal rate, decimal gross, decimal imputation, decimal foreign)
    {
        var source = rate > 0 ? DividendTaxSource.Foreign : DividendTaxSource.NewZealand;
        var amounts = DividendTerms.Create(amount, source, credit, rate, null).SourceAmounts(quantity, 2);

        amounts.ShouldBe(new DividendSourceAmounts(gross, imputation, foreign));
    }

    [Test]
    public void SourceAmounts_RoundAwayFromZeroAtTheMidpoint()
    {
        // 0.5 × 0.01 = 0.005 → 0.01; foreign tax 15 % of 0.01 = 0.0015 → 0.00.
        var amounts = DividendTerms.Create(0.01m, DividendTaxSource.Foreign, 0m, 15m, null).SourceAmounts(0.5m, 2);

        amounts.Gross.ShouldBe(0.01m);
        amounts.ForeignTax.ShouldBe(0m);
    }

    [Test]
    public void SourceAmounts_ForeignTax_IsTakenFromTheRoundedGross()
    {
        // 3 × 0.335 = 1.005 → 1.01 gross; 50 % of 1.01 = 0.505 → 0.51.
        var amounts = DividendTerms.Create(0.335m, DividendTaxSource.Foreign, 0m, 50m, null).SourceAmounts(3m, 2);

        amounts.Gross.ShouldBe(1.01m);
        amounts.ForeignTax.ShouldBe(0.51m);
    }

    [Test]
    public void SourceAmounts_ATinyHolding_HasAGrossOfZero() =>
        DividendTerms.Create(0.72m, DividendTaxSource.NewZealand, 0.28m, 0m, null).SourceAmounts(0.001m, 2)
            .Gross.ShouldBe(0m);
}
