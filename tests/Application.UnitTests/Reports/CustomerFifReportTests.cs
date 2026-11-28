using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Application.UnitTests.Reports.FifReportFixture;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>
/// Fif-report §4.3 to §4.6: the header and sections 1 to 5 of the content <see cref="CustomerFifReport.Build"/> gives
/// for a tax year read.
/// </summary>
public class CustomerFifReportTests
{
    private const string ThisAppliesIf =
        "These figures apply if the FIF rules apply to you. The threshold test above is one part of that question.";

    private static readonly ThresholdPeak PeakInOctober =
        new(50_046.40m, new DateTimeOffset(2025, 10, 9, 19, 30, 0, TimeSpan.Zero), Aaa, null);

    [Test]
    public void TheWorkedExample_Header()
    {
        var content = Build(Example);

        content.Title.ShouldBe("FIF Report");
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
        content.Complete.ShouldBeTrue();
        content.CustomerName.ShouldBe("Cee Customer");
        content.FooterTitle.ShouldBe("FIF Report, tax year 2026");
    }

    [Test]
    public void TheWorkedExample_HasTheNineSectionsInOrder()
    {
        Build(Example).Sections.Select(section => section.Title).ShouldBe([
            "1. Summary",
            "2. About this report",
            "3. Holdings and classification",
            "4. Threshold test",
            "5. FIF income",
            "6. Each holding",
            "7. Tax taken from dividends",
            "8. Prices, rates and values used",
            "9. What to know about these figures"
        ]);
    }

    [Test]
    public void TheWorkedExample_Summary()
    {
        Build(Example).Section(1).Texts().ShouldBe([
            "Threshold test: Not above the threshold. The total cost of your attributing interests was at most 12355.70, at the start of the tax year. The threshold for tax year 2026 is 50000.00.",
            "FIF income, fair dividend rate method: 655.42",
            "FIF income, comparative value method: 1740.87",
            "Lower: fair dividend rate",
            ThisAppliesIf,
            "Holdings: 1 attributing interest, 0 exempt or not foreign, 0 not classified."
        ]);
    }

    [Test]
    public void AboutThisReport_HasTheSevenStatementsWithExternalFigures()
    {
        Build(Example).Section(2).Texts().ShouldBe([
            "This report is not tax advice. It does not say whether the foreign investment fund (FIF) rules apply to you, which method to use, or what tax you owe.",
            "The FIF rules apply to a New Zealand tax resident who is not a transitional resident. Your tax residency on file is New Zealand, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year.",
            "This report covers the holdings recorded on this platform. Shares you hold elsewhere are in it only if your adviser has recorded them here. Shares that are not recorded count toward the threshold all the same.",
            "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.",
            "Both methods are shown. A person uses one method for all the holdings the totals cover in a tax year. The choice is yours.",
            "Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 8 lists every price and rate used.",
            "This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a transaction is entered, changed in date or reversed after that time, or when a price or a rate is added."
        ]);
    }

    [Test]
    public void AboutThisReport_WithoutExternalFigures_HasNoStatement4()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with { Holdings = [aaa with { Notices = aaa.Notices.Where(n => n != TaxNotice.ExternalFigures).ToList() }] };

        var texts = Build(year).Section(2).Texts();

        texts.Count.ShouldBe(6);
        texts.ShouldNotContain(text => text.StartsWith("Held elsewhere", StringComparison.Ordinal));
    }

    [Test]
    public void TheWorkedExample_HoldingsAndClassification()
    {
        var section = Build(Example).Section(3);

        section.Blocks.ShouldBe([
            new CustomerFifText("Attributing interests", Heading: true),
            section.Tables()[0],
            new CustomerFifText("Exempt or not foreign", Heading: true),
            new CustomerFifText("None."),
            new CustomerFifText("These holdings are not in the FIF calculation. Their dividends and sales are taxed under the ordinary rules and are not in this report."),
            new CustomerFifText("Not classified", Heading: true),
            new CustomerFifText("None."),
            new CustomerFifText("A holding that is not classified may be an attributing interest. Ask your adviser to have it classified.")
        ]);
        var table = section.Tables().Single();
        table.Columns.ShouldBe(["Symbol", "Name", "Issuer country", "Treatment", "Classified from", "Note", "Opening", "Closing", "FIF income"]);
        table.Rows.ShouldBe([
            ["AAA", "Alpha Inc", "United States of America", "Attributing interest", "classified from tax year 2025", "", "140", "240.8", "computed"]
        ]);
    }

    [Test]
    public void ThreeGroups_EachHoldingInItsGroup_AndAnExemptHoldingInNoLaterSection()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var eee = aaa.As(Eee, new TaxTreatment(FifTreatment.ExemptOther, 2024, "A unit trust exempt as an Australian unit trust", "AU"),
            new TaxHoldingIncome(Eee, FifIncomeStatus.NotRequired, null, null, null, null));
        var nnn = aaa.As(Nnn, TaxTreatment.NotClassified(), new TaxHoldingIncome(Nnn, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified, null, null, null))
            with { Incomplete = [TaxIncompleteReason.NotClassified] };
        var asx = aaa.As(Bbb, new TaxTreatment(FifTreatment.ExemptAsxListed, 2026, null, "AU"),
            new TaxHoldingIncome(Bbb, FifIncomeStatus.NotRequired, null, null, null, null));
        year = year with { Holdings = [nnn, eee, aaa, asx] };

        var content = Build(year);

        var section = content.Section(3);
        section.Texts().ShouldBe([
            "Attributing interests",
            "Exempt or not foreign",
            "These holdings are not in the FIF calculation. Their dividends and sales are taxed under the ordinary rules and are not in this report.",
            "Not classified",
            "A holding that is not classified may be an attributing interest. Ask your adviser to have it classified."
        ]);
        var tables = section.Tables();
        tables.Count.ShouldBe(3);
        tables[0].Rows.Select(row => row[0]).ShouldBe(["AAA"]);
        tables[1].Rows.ShouldBe([
            ["BBB", "Bravo Inc", "Australia", "Exempt: ASX-listed Australian share", "classified from tax year 2026", "", "140", "240.8", "not in the FIF rules"],
            ["EEE", "Echo Fund", "Australia", "Exempt: A unit trust exempt as an Australian unit trust", "classified from tax year 2024", "", "140", "240.8", "not in the FIF rules"]
        ]);
        tables[2].Rows.ShouldBe([
            ["NNN", "November Ltd", "not stated", "Not classified", "", "", "140", "240.8", "not computed"]
        ]);
        content.Section(1).Texts().Last().ShouldBe("Holdings: 1 attributing interest, 2 exempt or not foreign, 1 not classified.");
        content.Sections.Skip(3).SelectMany(s => s.Blocks).OfType<CustomerFifTable>()
            .SelectMany(table => table.Rows).ShouldNotContain(row => row.Contains("EEE") || row.Contains("BBB"));
        content.Sections.Skip(3).SelectMany(s => s.Texts())
            .ShouldNotContain(text => text.Contains("EEE") || text.Contains("Echo Fund") || text.Contains("BBB"));
    }

    [Test]
    public void ANoteOtherThanAnExemption_IsInTheNoteColumn()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with { Holdings = [aaa with { Treatment = aaa.Treatment with { Note = "Checked in the IR tool" } }] };

        Build(year).Section(3).Tables()[0].Rows[0][5].ShouldBe("Checked in the IR tool");
    }

    [Test]
    public void TheWorkedExample_ThresholdTest()
    {
        var section = Build(Example).Section(4);

        section.Texts().ShouldBe([
            "Threshold for tax year 2026: 50000.00",
            "Total cost at the start: 12355.70    Highest: 12355.70, at the start of the tax year    At the end: 10792.33",
            "Not above the threshold. The total cost of your attributing interests was at most 12355.70, at the start of the tax year. The threshold for tax year 2026 is 50000.00.",
            "A person at or below the threshold does not apply the FIF rules unless they choose to. The ordinary tax rules then apply to their dividends."
        ]);
        var tables = section.Tables();
        tables[0].Columns.ShouldBe(["Symbol", "Tax cost at the start", "Tax cost at the end"]);
        tables[0].Rows.ShouldBe([["AAA", "12355.70", "10792.33"]]);
        tables[1].Columns.ShouldBe(["Time", "Symbol", "Event", "Change", "Total after"]);
        tables[1].Rows.ShouldBe([
            ["2025-04-01 09:15", "AAA", "Sale", "-2647.65", "9708.05"],
            ["2025-09-15 14:30", "AAA", "Purchase", "+37.63", "9745.68"],
            ["2026-02-03 10:40", "AAA", "Purchase", "+1046.65", "10792.33"]
        ]);
    }

    [Test]
    public void Above_Complete_TheSentenceWithTheTimeOfThePeak()
    {
        var year = Example with { Threshold = Example.Threshold with { Outcome = ThresholdOutcome.Above, Peak = PeakInOctober } };

        var content = Build(year);

        const string sentence = "Above the threshold. The total cost of your attributing interests reached 50046.40 on 2025-10-10 08:30. The threshold for tax year 2026 is 50000.00. Above it at any time in the year, every attributing interest is in the FIF rules for the whole year.";
        content.Section(1).Texts()[0].ShouldBe("Threshold test: " + sentence);
        content.Section(4).Texts().ShouldContain(sentence);
        content.Section(4).Texts().ShouldContain("Total cost at the start: 12355.70    Highest: 50046.40, on 2025-10-10 08:30    At the end: 10792.33");
        content.Section(4).Texts().ShouldNotContain(text => text.StartsWith("A person at or below", StringComparison.Ordinal));
    }

    [Test]
    public void Above_Incomplete_TheKnownCostsCanOnlyAdd()
    {
        var year = Example with
        {
            Threshold = Example.Threshold with
            {
                Outcome = ThresholdOutcome.Above,
                Peak = PeakInOctober,
                Incomplete = [new ThresholdIncomplete(ThresholdIncompleteReason.CostUnknown, Aaa)]
            }
        };

        var content = Build(year);

        content.Section(1).Texts()[0].ShouldBe("Threshold test: Above the threshold. The costs that are known reached 50046.40 on 2025-10-10 08:30. The threshold for tax year 2026 is 50000.00. Some costs are not known. They can only add to the total.");
        content.Section(4).Texts().ShouldContain("Incomplete: see section 9.");
    }

    [Test]
    public void Unknown_WithAThreshold_TheTestCouldNotBeCompleted()
    {
        var year = Example with
        {
            Threshold = Example.Threshold with
            {
                Outcome = ThresholdOutcome.Unknown,
                Incomplete = [new ThresholdIncomplete(ThresholdIncompleteReason.NotClassified, Aaa)]
            }
        };

        Build(year).Section(1).Texts()[0].ShouldBe("Threshold test: The threshold test could not be completed. The costs that are known reached 12355.70 at the start of the tax year. The threshold for tax year 2026 is 50000.00. Some costs are not known, so the total may have been above it.");
    }

    [Test]
    public void Unknown_WithNoThreshold_NoThresholdIsSet()
    {
        var year = Example with
        {
            TaxYear = 2027,
            Threshold = Example.Threshold with
            {
                Amount = null,
                Outcome = ThresholdOutcome.Unknown,
                Incomplete = [new ThresholdIncomplete(ThresholdIncompleteReason.NoThreshold, null)]
            }
        };

        var content = Build(year);

        content.Section(1).Texts()[0].ShouldBe("Threshold test: No FIF threshold is set on this platform for tax year 2027. The total cost of your attributing interests that is known reached 12355.70 at the start of the tax year.");
        content.Section(4).Texts()[0].ShouldBe("No FIF threshold is set on this platform for tax year 2027.");
        content.Section(1).Texts().ShouldContain("FIF income, fair dividend rate method: 655.42");
        content.Section(1).Texts().ShouldContain("FIF income, comparative value method: 1740.87");
    }

    [Test]
    public void APeakAtTheStartAndNoStep_TheTotalDidNotChange()
    {
        var year = Example with { Threshold = Example.Threshold with { Steps = [] } };

        var section = Build(year).Section(4);

        section.Texts().ShouldContain("The total cost did not change during the tax year.");
        section.Tables().Count.ShouldBe(1);
    }

    [Test]
    public void AnUnknownTaxCost_IsNotKnownWithItsCause()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var unknown = new TaxCost(
            TaxCostStatus.Unknown, null, null,
            new TaxCostUnknown(TaxCostUnknownCause.NoCarriedCost, 3, new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), Guid.Empty));
        year = year with { Holdings = [aaa with { Cost = unknown }] };

        Build(year).Section(4).Tables()[0].Rows.ShouldBe([
            ["AAA", "not known: it was transferred in with no cost stated since 2025-06-01 12:00", "not known: it was transferred in with no cost stated since 2025-06-01 12:00"]
        ]);
    }

    [Test]
    public void TheWorkedExample_FifIncome()
    {
        var section = Build(Example).Section(5);

        section.Texts().ShouldBe([
            "Fair dividend rate method (rate 0.05)",
            "Opening value 13108.48    0.05 x opening value 655.42",
            "Peak holding method amounts 0.00    Quick sale gain amounts 0.00    Quick sale adjustment 0.00",
            "FIF income 655.42",
            "Comparative value method",
            "Total 1740.87    FIF income 1740.87",
            "The quick sale adjustment is the lesser of the two totals over all holdings, not a sum per holding."
        ]);
        var table = section.Tables().Single();
        table.Columns.ShouldBe(["Symbol", "FIF income", "Opening value", "FDR amount", "Peak holding method amount", "Quick sale gain amount", "CV amount"]);
        table.Rows.ShouldBe([["AAA", "computed", "13108.48", "655.42", "0.00", "0.00", "1740.87"]]);
    }

    [Test]
    public void ACvTotalBelowZero_GivesAnIncomeOfZero()
    {
        var year = Example with { FifIncome = Example.FifIncome with { Cv = new FifCvTotals(-120.00m, 0.00m), Lower = FifLowerMethod.Cv } };

        var content = Build(year);

        content.Section(5).Texts().ShouldContain("Total -120.00    FIF income 0.00");
        content.Section(5).Texts().ShouldContain("The total is below zero. It gives an income of 0.00 and cannot be used elsewhere.");
        content.Section(1).Texts().ShouldContain("Lower: comparative value");
    }

    [Test]
    public void TwoEqualTotals_AreSaidToBeEqual()
    {
        var year = Example with { FifIncome = Example.FifIncome with { Lower = FifLowerMethod.Equal } };

        Build(year).Section(1).Texts().ShouldContain("The two totals are equal");
    }

    [Test]
    public void TotalsGiven_WithAHoldingWhereFdrIsNotAllowed_SayWhatTheyDoNotInclude()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var sss = aaa.As(Sss, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "US"),
            new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null));
        year = year with
        {
            Holdings = [aaa, sss],
            FifIncome = year.FifIncome with
            {
                Complete = false,
                Incomplete = [new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Sss)]
            }
        };

        var content = Build(year);

        content.Section(1).Texts().ShouldBe([
            "Threshold test: Not above the threshold. The total cost of your attributing interests was at most 12355.70, at the start of the tax year. The threshold for tax year 2026 is 50000.00.",
            "FIF income, fair dividend rate method: 655.42",
            "FIF income, comparative value method: 1740.87",
            "Lower: fair dividend rate",
            "These totals do not include SSS: the fair dividend rate method is not allowed for it. Its income is worked out separately and added to the method you use.",
            ThisAppliesIf,
            "Holdings: 2 attributing interests, 0 exempt or not foreign, 0 not classified."
        ]);
        content.Section(3).Tables()[0].Rows[1].ShouldBe(
            ["SSS", "Sierra Fund", "United States of America", "Attributing interest, fair dividend rate not allowed", "classified from tax year 2025", "", "140", "240.8", "not computed"]);
        content.Section(5).Tables().Single().Rows[1].ShouldBe(["SSS", "not computed", "", "", "", "", ""]);
        content.Header.Last().ShouldBe("Status: Incomplete. 1 of 2 holdings are affected. See section 9.");
    }

    [Test]
    public void TwoHoldingsWhereFdrIsNotAllowed_AreThem()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var sss = aaa.As(Sss, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "US"),
            new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null));
        var bbb = aaa.As(Bbb, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "US"),
            new TaxHoldingIncome(Bbb, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null));
        year = year with
        {
            Holdings = [aaa, sss, bbb],
            FifIncome = year.FifIncome with
            {
                Complete = false,
                Incomplete = [new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Sss), new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Bbb)]
            }
        };

        Build(year).Section(1).Texts().ShouldContain(
            "These totals do not include BBB, SSS: the fair dividend rate method is not allowed for them. Their income is worked out separately and added to the method you use.");
    }

    [Test]
    public void TotalsNotGiven_SayWhichHoldingsCouldNotBeComputed_AndNoFigureTakesTheirPlace()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var notComputed = aaa with
        {
            Incomplete = [TaxIncompleteReason.ClosingNotValued],
            Income = new TaxHoldingIncome(Aaa, FifIncomeStatus.NotComputed, FifIncomeReason.HoldingIncomplete, null, null, null)
        };
        year = year with
        {
            Holdings = [notComputed],
            Complete = false,
            IncompleteCount = 1,
            FifIncome = year.FifIncome with
            {
                Fdr = null,
                Cv = null,
                Lower = null,
                Complete = false,
                Incomplete = [new FifIncomeIncomplete(FifIncomeReason.HoldingIncomplete, Aaa)]
            }
        };

        var content = Build(year);

        const string notGiven = "FIF income totals are not given. 1 holding that belongs in them could not be computed: AAA. A total without them would be neither a minimum nor an estimate.";
        content.Section(1).Texts().ShouldBe([
            "Threshold test: Not above the threshold. The total cost of your attributing interests was at most 12355.70, at the start of the tax year. The threshold for tax year 2026 is 50000.00.",
            notGiven,
            ThisAppliesIf,
            "Holdings: 1 attributing interest, 0 exempt or not foreign, 0 not classified."
        ]);
        content.Section(5).Texts().ShouldBe([
            notGiven,
            "The quick sale adjustment is the lesser of the two totals over all holdings, not a sum per holding."
        ]);
        content.Section(5).Tables().Single().Rows.ShouldBe([["AAA", "not computed", "", "", "", "", ""]]);
        content.Header.Last().ShouldBe("Status: Incomplete. 1 of 1 holdings are affected. See section 9.");
        content.Complete.ShouldBeFalse();
    }

    [Test]
    public void TwoHoldingsNotComputed_AreHoldingsThatBelong()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var nnn = aaa.As(Nnn, TaxTreatment.NotClassified("US"), new TaxHoldingIncome(Nnn, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified, null, null, null))
            with { Incomplete = [TaxIncompleteReason.NotClassified] };
        var notComputed = aaa with
        {
            Incomplete = [TaxIncompleteReason.ClosingNotValued],
            Income = new TaxHoldingIncome(Aaa, FifIncomeStatus.NotComputed, FifIncomeReason.HoldingIncomplete, null, null, null)
        };
        year = year with
        {
            Holdings = [nnn, notComputed],
            Complete = false,
            FifIncome = year.FifIncome with
            {
                Fdr = null,
                Cv = null,
                Lower = null,
                Complete = false,
                Incomplete =
                [
                    new FifIncomeIncomplete(FifIncomeReason.NotClassified, Nnn),
                    new FifIncomeIncomplete(FifIncomeReason.HoldingIncomplete, Aaa)
                ]
            }
        };

        var content = Build(year);

        content.Section(1).Texts().ShouldContain(
            "FIF income totals are not given. 2 holdings that belong in them could not be computed: AAA, NNN. A total without them would be neither a minimum nor an estimate.");
        content.Header.Last().ShouldBe("Status: Incomplete. 2 of 2 holdings are affected. See section 9.");
    }

    [Test]
    public void AThresholdWithoutValue_MakesTheReportIncompleteWithNoHoldingAffected()
    {
        var year = Example with
        {
            TaxYear = 2027,
            Threshold = Example.Threshold with
            {
                Amount = null,
                Outcome = ThresholdOutcome.Unknown,
                Incomplete = [new ThresholdIncomplete(ThresholdIncompleteReason.NoThreshold, null)]
            }
        };

        var content = Build(year);

        content.Header.ShouldContain("Status: Incomplete. 0 of 1 holdings are affected. See section 9.");
        content.Complete.ShouldBeFalse();
    }

    [Test]
    public void ACustomerWithNothing_HasTheHeaderSection2AndOneLine()
    {
        var year = Example with
        {
            Holdings = [],
            Threshold = Example.Threshold with { Opening = 0m, Closing = 0m, Peak = new ThresholdPeak(0m, Example.From, null, null), Steps = [] },
            FifIncome = Example.FifIncome with { Fdr = new FifFdrTotals(0m, 0m, 0m, 0m, 0m, 0m), Cv = new FifCvTotals(0m, 0m), Lower = FifLowerMethod.Equal, Holdings = [] }
        };

        var content = Build(year);

        content.Complete.ShouldBeTrue();
        content.Header.Last().ShouldBe("Status: Complete");
        content.Sections.Select(section => section.Title).ShouldBe(["1. Summary", "2. About this report"]);
        content.Section(1).Blocks.ShouldBe([new CustomerFifText("No holdings in this tax year.")]);
        content.Section(2).Texts().Count.ShouldBe(6);
    }
}
