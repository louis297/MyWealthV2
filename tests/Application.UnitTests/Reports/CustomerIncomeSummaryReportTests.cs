using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Application.UnitTests.Reports.IncomeSummaryFixture;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>
/// Income-summary R22 to R28, R32, §4.6: the header, the statements and sections 1 to 4 of the content
/// <see cref="CustomerIncomeSummaryReport.Build"/> gives for a tax year read.
/// </summary>
public class CustomerIncomeSummaryReportTests
{
    private const string Credits =
        "RWT and imputation credits are credits against New Zealand tax. Overseas tax paid may give a foreign tax credit. The credit is limited, and this report does not work it out.";

    private const string NewZealandSentence =
        "Inland Revenue's individual return asks for New Zealand dividends as the gross dividend, with the imputation credits and the RWT.";

    private const string ForeignExemptSentence =
        "These holdings are classified as exempt from the FIF rules. Their dividends are overseas income under the ordinary tax rules. Inland Revenue's overseas income summary (IR1261) asks for overseas income by income type and by country, with the overseas tax paid. RWT is New Zealand tax, not overseas tax.";

    private const string AttributingSentence =
        "If the FIF rules apply to you, these dividends are not returned as dividends: FIF income is returned in their place. If the FIF rules do not apply to you, they are overseas income under the ordinary tax rules, like the dividends of the group above. This report does not say which is the case. The FIF Report of the same tax year shows the threshold test and FIF income.";

    private const string NotClassifiedSentence =
        "These holdings are not classified for tax year 2026. This report cannot say which of the groups above their dividends belong to. Ask your adviser to have them classified.";

    private const string GrossNote =
        "Dividends are before any tax was taken. Gross dividends are the dividends with their imputation credits.";

    private const string CountryOfInterest =
        "Interest from a New Zealand payer is New Zealand interest. Interest from overseas is overseas income. The platform does not record the country of an account, so this report does not say which of the two an amount is.";

    private const string InterestChargedSentence =
        "Interest charged to an account is listed here and is not taken off the interest received. Whether it reduces your income depends on what it was charged for.";

    private static readonly string[] SummaryColumns =
        ["", "Payments", "Amount", "Imputation credits", "Gross dividends", "Foreign tax", "RWT", "NRWT"];

    private static readonly string[] DividendColumns =
    [
        "Symbol", "Name", "Issuer country", "Classification", "Payments", "Dividends", "Imputation credits", "Gross dividends",
        "Foreign tax", "RWT", "NRWT"
    ];

    [Test]
    public void TheWorkedExample_Header()
    {
        var content = Build(Example);

        content.Title.ShouldBe("Tax Year Income Summary");
        content.Header.ShouldBe([
            "Tenant: Demo Advisory",
            "Customer: Cee Customer",
            "Tax year: 2026 (1 April 2025 to 31 March 2026, New Zealand time)",
            "Tax year in UTC: 2025-03-31 11:00 to 2026-03-31 11:00",
            "Generated: 2026-11-28 16:12 New Zealand time (2026-11-28 03:12:45 UTC)",
            "Ledger read at: 2026-11-28 16:12 New Zealand time (2026-11-28 03:12:45 UTC)",
            "Tax residency on file: New Zealand",
            "All amounts are in New Zealand dollars (NZD). All times are New Zealand time.",
            "Status: Complete"
        ]);
        (content.Complete, content.CustomerName, content.TaxYear).ShouldBe((true, "Cee Customer", 2026));
        content.FooterTitle.ShouldBe("Tax Year Income Summary, tax year 2026");
    }

    [Test]
    public void AnOverseasCustomer_HasTheResidencyInTheHeaderAndTheOverseasStatement()
    {
        var content = Build(Example, TaxResidency.Overseas);

        content.Header.ShouldContain("Tax residency on file: Overseas");
        content.Section(2).Texts()[1].ShouldBe(
            "Your tax residency on file is Overseas, as recorded when this report was made. What this report says about the return is written for a New Zealand tax resident. A person who is not a New Zealand tax resident files a different return.");
    }

    [Test]
    public void AboutThisReport_HasTheEightStatementsWithExternalFigures()
    {
        Build(Example).Section(2).Texts().ShouldBe([
            "This report is not tax advice. It lists the dividends and interest recorded for you on this platform and the tax taken from them. It does not say what income you return or what tax you owe.",
            "Your tax residency on file is New Zealand, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year.",
            "This report covers the accounts recorded on this platform. Dividends and interest you were paid elsewhere are in it only if your adviser has recorded them here. It lists no other income: no gain on a sale, no rent and no salary.",
            "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.",
            "A payment is in the tax year in which it reached your account. Interest earned in March and paid in April is in the next tax year.",
            "Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 6 lists every rate used.",
            "This report is not a form of Inland Revenue and not a withholding tax certificate. The payer of a dividend or of interest may send you a statement of its own.",
            "This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a payment is entered, changed in date or reversed after that time, or when a rate is added."
        ]);
        Build(Example).Section(2).Title.ShouldBe("2. About this report");
    }

    [Test]
    public void WithoutExternalFigures_TheStatementOfHeldElsewhereIsLeftOut()
    {
        var texts = Build(Year([T4, I3])).Section(2).Texts();

        texts.Count.ShouldBe(7);
        texts.ShouldNotContain(text => text.StartsWith("Held elsewhere", StringComparison.Ordinal));
    }

    [Test]
    public void TheWorkedExample_Summary()
    {
        var section = Build(Example).Section(1);

        section.Title.ShouldBe("1. Summary");
        var table = section.Table(0);
        table.Columns.ShouldBe(SummaryColumns);
        table.Rows.ShouldBe([
            ["New Zealand companies", "1", "720.00", "280.00", "1000.00", "0.00", "50.00", "0.00"],
            ["Foreign companies outside the FIF rules", "1", "327.00", "0.00", "327.00", "49.05", "0.00", "0.00"],
            ["Attributing interests", "2", "117.95", "0.00", "117.95", "17.69", "21.23", "0.00"],
            ["All dividends", "4", "1164.95", "280.00", "1444.95", "66.74", "71.23", "0.00"],
            ["Interest received", "4", "133.70", "", "", "0.00", "25.51", "0.00"],
            ["Tax taken and credits", "", "", "280.00", "", "66.74", "96.74", "0.00"]
        ]);
        section.Texts().ShouldBe([
            "Interest charged: 8.00. It is not taken off the interest received.",
            Credits
        ]);
    }

    [Test]
    public void TheSummary_HasARowForAGroupOnlyWhenItHasADividend_AndNoLineOnInterestChargedWithoutIt()
    {
        var treatments = new Dictionary<int, TaxTreatment>();

        var section = Build(Year([T4, I1], treatments: treatments)).Section(1);

        section.Table(0).Rows.Select(row => row[0]).ShouldBe(["Not classified", "All dividends", "Interest received", "Tax taken and credits"]);
        section.Texts().ShouldBe([Credits]);
    }

    [Test]
    public void TheWorkedExample_Dividends()
    {
        var section = Build(Example).Section(3);

        section.Title.ShouldBe("3. Dividends");
        section.Lines().ShouldBe([
            "# New Zealand companies",
            NewZealandSentence,
            "KIW | Kiwi Power Ltd | New Zealand | Not foreign (New Zealand) | 1 | 720.00 | 280.00 | 1000.00 | 0.00 | 50.00 | 0.00",
            "Total |  |  |  | 1 | 720.00 | 280.00 | 1000.00 | 0.00 | 50.00 | 0.00",
            "# Foreign companies outside the FIF rules",
            ForeignExemptSentence,
            "OZB | Oz Bank Ltd | Australia | Exempt: ASX-listed Australian share | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "Australia |  |  |  | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "Total |  |  |  | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "# Attributing interests",
            AttributingSentence,
            "AAA | Alpha Inc | United States of America | Attributing interest | 2 | 117.95 | 0.00 | 117.95 | 17.69 | 21.23 | 0.00",
            "United States of America |  |  |  | 2 | 117.95 | 0.00 | 117.95 | 17.69 | 21.23 | 0.00",
            "Total |  |  |  | 2 | 117.95 | 0.00 | 117.95 | 17.69 | 21.23 | 0.00",
            "# Not classified",
            "None.",
            GrossNote
        ]);
        section.Tables().ShouldAllBe(table => table.Columns.SequenceEqual(DividendColumns));
    }

    [Test]
    public void ANotClassifiedHoldingAndAHoldingWithNoCountry_ArePrintedWithTheirWords()
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Ozb] = new(FifTreatment.ExemptOther, 2026, "Listed in Australia", null) };

        var section = Build(Year([T4, D2], treatments: treatments)).Section(3);

        section.Lines().ShouldBe([
            "# New Zealand companies",
            "None.",
            "# Foreign companies outside the FIF rules",
            ForeignExemptSentence,
            "OZB | Oz Bank Ltd | not stated | Exempt: Listed in Australia | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "Country not stated |  |  |  | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "Total |  |  |  | 1 | 327.00 | 0.00 | 327.00 | 49.05 | 0.00 | 0.00",
            "# Attributing interests",
            "None.",
            "# Not classified",
            NotClassifiedSentence,
            "AAA | Alpha Inc | not stated | Not classified | 1 | 59.15 | 0.00 | 59.15 | 8.87 | 10.65 | 0.00",
            "Total |  |  |  | 1 | 59.15 | 0.00 | 59.15 | 8.87 | 10.65 | 0.00",
            GrossNote
        ]);
    }

    [Test]
    public void WithNoDividend_TheDividendsSectionSaysSo() =>
        Build(Year([I1])).Section(3).Lines().ShouldBe(["No dividends in this tax year."]);

    [Test]
    public void TheWorkedExample_Interest()
    {
        var section = Build(Example).Section(4);

        section.Title.ShouldBe("4. Interest");
        var table = section.Table(0);
        table.Columns.ShouldBe(["Account", "Custody", "Currency", "Payments", "Interest received", "Interest charged", "RWT", "NRWT", "Foreign tax"]);
        table.Rows.ShouldBe([
            ["Old broker", "Held elsewhere", "NZD", "2", "120.00", "8.00", "21.00", "0.00", "0.00"],
            ["Trading", "Held on platform", "USD", "2", "13.70", "0.00", "4.51", "0.00", "0.00"],
            ["Total", "", "", "4", "133.70", "8.00", "25.51", "0.00", "0.00"]
        ]);
        section.Texts().ShouldBe([CountryOfInterest, InterestChargedSentence]);
    }

    [Test]
    public void WithoutInterestCharged_OnlyTheSentenceOnTheCountryIsPrinted() =>
        Build(Year([I1, I3])).Section(4).Texts().ShouldBe([CountryOfInterest]);

    [Test]
    public void WithNoInterest_TheInterestSectionSaysSo() =>
        Build(Year([D1])).Section(4).Lines().ShouldBe(["No interest in this tax year."]);

    [Test]
    public void AMissingFigure_IsNotAvailable_AndTheReportIsIncomplete()
    {
        var content = Build(Year([D1, I3], rates: []));

        content.Complete.ShouldBeFalse();
        content.Header.LastOrDefault().ShouldBe("Status: Incomplete. See section 7.");
        content.Section(1).Table(0).Rows.ShouldBe([
            ["New Zealand companies", "1", "720.00", "280.00", "1000.00", "0.00", "50.00", "0.00"],
            ["All dividends", "1", "720.00", "280.00", "1000.00", "0.00", "50.00", "0.00"],
            ["Interest received", "1", "not available", "", "", "not available", "not available", "not available"],
            ["Tax taken and credits", "", "", "280.00", "", "not available", "not available", "not available"]
        ]);
        content.Section(4).Table(0).Rows[0].ShouldBe(
            ["Trading", "Held on platform", "USD", "1", "not available", "not available", "not available", "not available", "not available"]);
    }

    [Test]
    public void NothingPaid_IsTheHeaderSectionTwoAndOneLine()
    {
        var content = Build(Year([T1]));

        content.Complete.ShouldBeTrue();
        content.Header.LastOrDefault().ShouldBe("Status: Complete");
        content.Sections.Select(section => section.Title).ShouldBe(["1. Summary", "2. About this report"]);
        content.Section(1).Lines().ShouldBe(["No dividends and no interest in this tax year."]);
    }
}
