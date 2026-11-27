using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Instruments;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Instrument-tax-attributes R10, R11 and the second table of §1.1.</summary>
public class InstrumentFifTreatmentForTests
{
    private const FifTreatment None = FifTreatment.NotClassified;
    private const FifTreatment Attributing = FifTreatment.Attributing;
    private const FifTreatment Asx = FifTreatment.ExemptAsxListed;

    [Test]
    public void Aapl_AnAttributingRowStandsForItsYearAndEveryLaterYear()
    {
        var aapl = With("US", (2027, Attributing));

        Years(aapl).ShouldBe([None, Attributing, Attributing, Attributing]);
    }

    [Test]
    public void Bhp_AnExemptionStandsForItsOwnYearOnly()
    {
        var bhp = With("AU", (2027, Asx));

        Years(bhp).ShouldBe([None, Asx, None, None]);
    }

    [Test]
    public void Bhp_CheckedForTwoYears()
    {
        var bhp = With("AU", (2027, Asx), (2028, Asx));

        Years(bhp).ShouldBe([None, Asx, Asx, None]);
    }

    [Test]
    public void ACompanyThatLeftTheAsxList()
    {
        var instrument = With("AU", (2026, Asx), (2027, Attributing));

        Years(instrument).ShouldBe([Asx, Attributing, Attributing, Attributing]);
    }

    [Test]
    public void AnInstrumentWithNoRow_IsNeverClassified()
    {
        var instrument = With(null);

        Years(instrument).ShouldBe([None, None, None, None]);
    }

    [Test]
    public void AYearBeforeTheFirstRow_IsNotClassified()
    {
        var instrument = With("JP", (2027, FifTreatment.AttributingNoFdr));

        instrument.FifTreatmentFor(2008).ShouldBe(None);
        instrument.FifTreatmentFor(2026).ShouldBe(None);
        instrument.FifTreatmentFor(2030).ShouldBe(FifTreatment.AttributingNoFdr);
    }

    [Test]
    public void AnExemptionRow_EndsWhatStoodBeforeIt()
    {
        var instrument = With("AU", (2026, Attributing), (2027, Asx));

        instrument.FifTreatmentFor(2026).ShouldBe(Attributing);
        instrument.FifTreatmentFor(2027).ShouldBe(Asx);
        instrument.FifTreatmentFor(2028).ShouldBe(None);
    }

    [Test]
    public void ExemptOther_StandsForOneYear()
    {
        var instrument = Vti();
        instrument.SetTaxAttributes("AU", [new FifTreatmentInput(2027, FifTreatment.ExemptOther, "EX 32 unit trust")], 2030);

        instrument.FifTreatmentFor(2027).ShouldBe(FifTreatment.ExemptOther);
        instrument.FifTreatmentFor(2028).ShouldBe(None);
    }

    [Test]
    public void NotForeign_StandsUntilTheNextRow()
    {
        var instrument = With("NZ", (2027, FifTreatment.NotForeign));

        instrument.FifTreatmentFor(2040).ShouldBe(FifTreatment.NotForeign);
    }

    [Test]
    public void RowFor_IsTheRowTheAnswerCameFrom()
    {
        var instrument = With("AU", (2026, Attributing), (2027, Asx), (2029, Attributing));

        instrument.FifTreatmentRowFor(2025).ShouldBeNull();
        instrument.FifTreatmentRowFor(2026).ShouldNotBeNull().TaxYear.ShouldBe(2026);
        instrument.FifTreatmentRowFor(2027).ShouldNotBeNull().TaxYear.ShouldBe(2027);
        instrument.FifTreatmentRowFor(2028).ShouldBeNull();
        instrument.FifTreatmentRowFor(2030).ShouldNotBeNull().TaxYear.ShouldBe(2029);
    }

    private static FifTreatment[] Years(Instrument instrument) =>
        [instrument.FifTreatmentFor(2026), instrument.FifTreatmentFor(2027), instrument.FifTreatmentFor(2028), instrument.FifTreatmentFor(2029)];

    private static Instrument With(string? country, params (int TaxYear, FifTreatment Treatment)[] rows)
    {
        var instrument = Vti();
        instrument.SetTaxAttributes(country, rows.Select(row => new FifTreatmentInput(row.TaxYear, row.Treatment, null)).ToList(), 2030);
        return instrument;
    }

    private static Instrument Vti() =>
        Instrument.Create(1, "VTI", "Vanguard Total Stock Market ETF", Currency.Create("USD", "United States Dollar", 2));
}
