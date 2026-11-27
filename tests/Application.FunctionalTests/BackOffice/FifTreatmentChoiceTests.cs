extern alias BackOfficeHost;

using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

/// <summary>Instrument-tax-attributes BO-I9, BO-I10, BO-I12: the FIF treatment select and texts.</summary>
public class FifTreatmentChoiceTests
{
    [Test]
    public void Values_AreTheFiveStoredTreatments() =>
        FifTreatmentChoice.Values.ShouldBe(["NotForeign", "ExemptAsxListed", "ExemptOther", "Attributing", "AttributingNoFdr"]);

    [TestCase("NotForeign", "Not foreign (New Zealand issuer)")]
    [TestCase("ExemptAsxListed", "Exempt: ASX-listed Australian share")]
    [TestCase("ExemptOther", "Exempt: other (name it in the note)")]
    [TestCase("Attributing", "Attributing interest")]
    [TestCase("AttributingNoFdr", "Attributing interest, FDR not allowed")]
    public void Label_ReadsAsBoI10(string value, string label) => FifTreatmentChoice.Label(value).ShouldBe(label);

    [Test]
    public void ListText_WithNoRow_IsNotClassified()
    {
        FifTreatmentChoice.ListText(null).ShouldBe("Not classified");
        FifTreatmentChoice.ListText([]).ShouldBe("Not classified");
    }

    [Test]
    public void ListText_ATreatmentThatStandsForLaterYears_ReadsFromItsYear() =>
        FifTreatmentChoice.ListText([new FifTreatmentItem(2027, "Attributing", null)]).ShouldBe("Attributing interest from 2027");

    [Test]
    public void ListText_AnExemption_ReadsItsYearOnly() =>
        FifTreatmentChoice.ListText([new FifTreatmentItem(2027, "ExemptOther", "EX 32")]).ShouldBe("Exempt: other (name it in the note) 2027");

    [Test]
    public void ListText_ShowsTheRowWithTheLatestTaxYear() =>
        FifTreatmentChoice.ListText(
        [
            new FifTreatmentItem(2028, "ExemptAsxListed", null),
            new FifTreatmentItem(2026, "Attributing", null),
            new FifTreatmentItem(2027, "ExemptAsxListed", null)
        ]).ShouldBe("Exempt: ASX-listed Australian share 2028");

    [Test]
    public void CountryLabel_ShowsTheNameAndTheCode()
    {
        IReadOnlyList<CountryItem> countries = [new("AU", "Australia"), new("NZ", "New Zealand")];

        FifTreatmentChoice.CountryLabel("NZ", countries).ShouldBe("New Zealand (NZ)");
        FifTreatmentChoice.CountryLabel("XX", countries).ShouldBe("XX");
        FifTreatmentChoice.CountryLabel(null, countries).ShouldBe("");
    }
}
