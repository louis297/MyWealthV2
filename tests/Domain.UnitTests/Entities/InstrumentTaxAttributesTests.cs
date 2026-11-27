using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Instruments;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Instrument-tax-attributes R5, R9, R12, R16, R20: <see cref="Instrument.SetTaxAttributes"/>.</summary>
public class InstrumentTaxAttributesTests
{
    private const int Latest = 2027;

    private const string TreatmentMessage =
        "Treatment must be NotForeign, ExemptAsxListed, ExemptOther, Attributing, or AttributingNoFdr.";

    [Test]
    public void FifTreatment_ExemptionsAndAttributingValues()
    {
        FifTreatment.ExemptAsxListed.IsExemption().ShouldBeTrue();
        FifTreatment.ExemptOther.IsExemption().ShouldBeTrue();
        FifTreatment.NotForeign.IsExemption().ShouldBeFalse();
        FifTreatment.Attributing.IsExemption().ShouldBeFalse();
        FifTreatment.AttributingNoFdr.IsExemption().ShouldBeFalse();
        FifTreatment.NotClassified.IsExemption().ShouldBeFalse();

        FifTreatment.Attributing.IsAttributing().ShouldBeTrue();
        FifTreatment.AttributingNoFdr.IsAttributing().ShouldBeTrue();
        FifTreatment.NotForeign.IsAttributing().ShouldBeFalse();
        FifTreatment.ExemptAsxListed.IsAttributing().ShouldBeFalse();
        FifTreatment.ExemptOther.IsAttributing().ShouldBeFalse();
        FifTreatment.NotClassified.IsAttributing().ShouldBeFalse();
    }

    [Test]
    public void NewInstrument_HasNoCountryAndNoRow()
    {
        var instrument = Vti();

        instrument.IssuerCountry.ShouldBeNull();
        instrument.FifTreatments.ShouldBeEmpty();
    }

    [Test]
    public void CountryAlone_IsStoredTrimmedAndUpperCase()
    {
        var instrument = Vti();

        instrument.SetTaxAttributes(" us ", [], Latest);

        instrument.IssuerCountry.ShouldBe("US");
        instrument.FifTreatments.ShouldBeEmpty();
    }

    [Test]
    public void RowsWithACountry_AreStoredWithTheInstrumentsTenant()
    {
        var instrument = Vti(tenantId: 7);

        instrument.SetTaxAttributes("AU",
        [
            new FifTreatmentInput(2028, FifTreatment.ExemptAsxListed, " Checked on 2027-04-06. "),
            new FifTreatmentInput(2027, FifTreatment.ExemptAsxListed, null)
        ], Latest + 1);

        instrument.IssuerCountry.ShouldBe("AU");
        instrument.FifTreatments.Select(row => (row.TaxYear, row.Treatment, row.Note)).OrderBy(row => row.TaxYear).ShouldBe(
        [
            (2027, FifTreatment.ExemptAsxListed, (string?)null),
            (2028, FifTreatment.ExemptAsxListed, "Checked on 2027-04-06.")
        ]);
        instrument.FifTreatments.ShouldAllBe(row => row.TenantId == 7);
    }

    [Test]
    public void EmptyNote_IsStoredAsNone()
    {
        var instrument = Vti();

        instrument.SetTaxAttributes("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, "   ")], Latest);

        instrument.FifTreatments.ShouldHaveSingleItem().Note.ShouldBeNull();
    }

    [Test]
    public void ACountryWithNoRow_IsAllowed()
    {
        var instrument = Vti();

        instrument.SetTaxAttributes("NZ", [], Latest);

        instrument.IssuerCountry.ShouldBe("NZ");
    }

    [Test]
    public void NewZealandWithNotForeign_AndAustraliaWithExemptAsxListed_AreAllowed()
    {
        var aia = Vti();
        aia.SetTaxAttributes("NZ", [new FifTreatmentInput(2027, FifTreatment.NotForeign, null)], Latest);
        aia.FifTreatments.ShouldHaveSingleItem().Treatment.ShouldBe(FifTreatment.NotForeign);

        var bhp = Vti();
        bhp.SetTaxAttributes("AU",
        [
            new FifTreatmentInput(2026, FifTreatment.Attributing, null),
            new FifTreatmentInput(2027, FifTreatment.ExemptAsxListed, null)
        ], Latest);
        bhp.FifTreatments.Count.ShouldBe(2);
    }

    [Test]
    public void ExemptOtherWithANote_IsAllowed()
    {
        var instrument = Vti();

        instrument.SetTaxAttributes("AU", [new FifTreatmentInput(2027, FifTreatment.ExemptOther, "EX 32 unit trust")], Latest);

        instrument.FifTreatments.ShouldHaveSingleItem().Note.ShouldBe("EX 32 unit trust");
    }

    [Test]
    public void SecondCall_UpdatesAddsAndRemovesRowsByTaxYear()
    {
        var instrument = Vti();
        instrument.SetTaxAttributes("AU",
        [
            new FifTreatmentInput(2026, FifTreatment.ExemptAsxListed, "first"),
            new FifTreatmentInput(2027, FifTreatment.ExemptAsxListed, null)
        ], Latest);
        instrument.FifTreatments.Count.ShouldBe(2);
        var kept = instrument.FifTreatments.Single(row => row.TaxYear == 2027);

        instrument.SetTaxAttributes("AU",
        [
            new FifTreatmentInput(2027, FifTreatment.Attributing, "left the list"),
            new FifTreatmentInput(2028, FifTreatment.Attributing, null)
        ], Latest + 1);

        instrument.FifTreatments.Select(row => (row.TaxYear, row.Treatment, row.Note)).OrderBy(row => row.TaxYear).ShouldBe(
        [
            (2027, FifTreatment.Attributing, "left the list"),
            (2028, FifTreatment.Attributing, (string?)null)
        ]);
        instrument.FifTreatments.ShouldContain(kept);
    }

    [Test]
    public void NullCountryAndNoRows_ClearEverything()
    {
        var instrument = Vti();
        instrument.SetTaxAttributes("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, null)], Latest);

        instrument.SetTaxAttributes(null, [], Latest);

        instrument.IssuerCountry.ShouldBeNull();
        instrument.FifTreatments.ShouldBeEmpty();
    }

    [Test]
    public void SetTaxAttributes_RaisesNoEvent()
    {
        var instrument = Vti();
        instrument.ClearDomainEvents();

        instrument.SetTaxAttributes("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, null)], Latest);

        instrument.DomainEvents.ShouldBeEmpty();
    }

    [TestCase("UK")]
    [TestCase("XX")]
    [TestCase("NZL")]
    [TestCase("")]
    [TestCase("N")]
    public void UnknownCountry_IsRefused(string country)
    {
        ShouldRefuse(country, [], ("IssuerCountry", "IssuerCountry must be an ISO 3166-1 alpha-2 country code."));
    }

    [Test]
    public void LowerCaseCountry_IsAcceptedAsUpperCase()
    {
        Instrument.TaxAttributeRefusals("gb", [], Latest).ShouldBeEmpty();
    }

    [TestCase(2007)]
    [TestCase(Latest + 1)]
    [TestCase(0)]
    public void TaxYearOutsideTheBounds_IsRefused(int taxYear)
    {
        ShouldRefuse("US", [new FifTreatmentInput(taxYear, FifTreatment.Attributing, null)],
            ("FifTreatments[0].TaxYear", $"TaxYear must be between 2008 and {Latest}."));
    }

    [Test]
    public void TaxYearOnTheBounds_IsAllowed()
    {
        Instrument.TaxAttributeRefusals("US",
        [
            new FifTreatmentInput(2008, FifTreatment.Attributing, null),
            new FifTreatmentInput(Latest, FifTreatment.Attributing, null)
        ], Latest).ShouldBeEmpty();
    }

    [Test]
    public void ATaxYearTwice_IsRefused()
    {
        ShouldRefuse("US",
        [
            new FifTreatmentInput(2027, FifTreatment.Attributing, null),
            new FifTreatmentInput(2027, FifTreatment.AttributingNoFdr, null)
        ], ("FifTreatments", "Each tax year can be entered once."));
    }

    [Test]
    public void MoreThanFiftyRows_IsRefused()
    {
        var rows = Enumerable.Range(1960, 51).Select(year => new FifTreatmentInput(year, FifTreatment.Attributing, null)).ToList();

        Instrument.TaxAttributeRefusals("US", rows, 2050)
            .ShouldContain(new TaxAttributeRefusal("FifTreatments", "At most 50 FIF treatments can be entered."));
        Should.Throw<DomainException>(() => Vti().SetTaxAttributes("US", rows, 2050));
    }

    [Test]
    public void FiftyRows_IsAllowed()
    {
        var rows = Enumerable.Range(2008, 50).Select(year => new FifTreatmentInput(year, FifTreatment.Attributing, null)).ToList();

        Instrument.TaxAttributeRefusals("US", rows, 2057).ShouldBeEmpty();
    }

    [TestCase(FifTreatment.NotClassified)]
    [TestCase((FifTreatment)6)]
    [TestCase((FifTreatment)(-1))]
    public void NotAStoredTreatment_IsRefused(FifTreatment treatment)
    {
        ShouldRefuse("US", [new FifTreatmentInput(2027, treatment, null)], ("FifTreatments[0].Treatment", TreatmentMessage));
    }

    [Test]
    public void NoteAbove500Characters_IsRefused()
    {
        ShouldRefuse("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, new string('n', 501))],
            ("FifTreatments[0].Note", "Note must be at most 500 characters."));
    }

    [Test]
    public void NoteOf500CharactersAfterTrim_IsAllowed()
    {
        Instrument.TaxAttributeRefusals("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, $" {new string('n', 500)} ")], Latest)
            .ShouldBeEmpty();
    }

    [TestCase(null)]
    [TestCase("  ")]
    public void ExemptOtherWithoutANote_IsRefused(string? note)
    {
        ShouldRefuse("AU", [new FifTreatmentInput(2027, FifTreatment.ExemptOther, note)],
            ("FifTreatments[0].Note", "Note is required for ExemptOther: name the exemption."));
    }

    [Test]
    public void ARowWithoutACountry_IsRefused()
    {
        ShouldRefuse(null, [new FifTreatmentInput(2027, FifTreatment.Attributing, null)],
            ("IssuerCountry", "IssuerCountry is required when a FIF treatment is set."));
    }

    [TestCase(FifTreatment.Attributing)]
    [TestCase(FifTreatment.AttributingNoFdr)]
    [TestCase(FifTreatment.ExemptAsxListed)]
    [TestCase(FifTreatment.ExemptOther)]
    public void NewZealandWithAnotherTreatment_IsRefused(FifTreatment treatment)
    {
        ShouldRefuse("NZ",
        [
            new FifTreatmentInput(2026, FifTreatment.NotForeign, null),
            new FifTreatmentInput(2027, treatment, "a note")
        ], ("FifTreatments[1].Treatment", "An issuer resident in New Zealand is NotForeign."));
    }

    [TestCase("US")]
    [TestCase("AU")]
    public void NotForeignWithAnotherCountry_IsRefused(string country)
    {
        ShouldRefuse(country, [new FifTreatmentInput(2027, FifTreatment.NotForeign, null)],
            ("FifTreatments[0].Treatment", "NotForeign is for an issuer resident in New Zealand."));
    }

    [TestCase("GB")]
    [TestCase("US")]
    public void ExemptAsxListedOutsideAustralia_IsRefused(string country)
    {
        ShouldRefuse(country, [new FifTreatmentInput(2027, FifTreatment.ExemptAsxListed, null)],
            ("FifTreatments[0].Treatment", "ExemptAsxListed is for an issuer resident in Australia."));
    }

    [Test]
    public void Refusals_ListEveryReasonOfTheBody()
    {
        var refusals = Instrument.TaxAttributeRefusals("US",
        [
            new FifTreatmentInput(2007, FifTreatment.Attributing, null),
            new FifTreatmentInput(2027, FifTreatment.NotForeign, null),
            new FifTreatmentInput(2027, FifTreatment.ExemptOther, null)
        ], Latest);

        refusals.ShouldBe(
        [
            new TaxAttributeRefusal("FifTreatments[0].TaxYear", "TaxYear must be between 2008 and 2027."),
            new TaxAttributeRefusal("FifTreatments[2].Note", "Note is required for ExemptOther: name the exemption."),
            new TaxAttributeRefusal("FifTreatments", "Each tax year can be entered once."),
            new TaxAttributeRefusal("FifTreatments[1].Treatment", "NotForeign is for an issuer resident in New Zealand.")
        ], ignoreOrder: true);
    }

    [Test]
    public void ARefusedWrite_ChangesNothing()
    {
        var instrument = Vti();
        instrument.SetTaxAttributes("US", [new FifTreatmentInput(2027, FifTreatment.Attributing, null)], Latest);

        Should.Throw<DomainException>(() =>
            instrument.SetTaxAttributes("NZ", [new FifTreatmentInput(2027, FifTreatment.Attributing, null)], Latest));

        instrument.IssuerCountry.ShouldBe("US");
        instrument.FifTreatments.ShouldHaveSingleItem().Treatment.ShouldBe(FifTreatment.Attributing);
    }

    private static void ShouldRefuse(string? country, IReadOnlyList<FifTreatmentInput> rows, (string Field, string Message) expected)
    {
        var refusals = Instrument.TaxAttributeRefusals(country, rows, Latest);
        refusals.ShouldContain(new TaxAttributeRefusal(expected.Field, expected.Message));

        var instrument = Vti();
        var exception = Should.Throw<DomainException>(() => instrument.SetTaxAttributes(country, rows, Latest));
        exception.Message.ShouldBe(refusals[0].Message);
        instrument.IssuerCountry.ShouldBeNull();
        instrument.FifTreatments.ShouldBeEmpty();
    }

    private static Instrument Vti(int tenantId = 1) =>
        Instrument.Create(tenantId, "VTI", "Vanguard Total Stock Market ETF", Currency.Create("USD", "United States Dollar", 2));
}
