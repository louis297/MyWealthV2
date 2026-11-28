using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Application.UnitTests.Reports.IncomeSummaryFixture;
using static MyWealthV2.Domain.UnitTests.Tax.IncomeWorkedExample;
using static MyWealthV2.Domain.UnitTests.Tax.TaxWorkedExample;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Income-summary R29 to R31, §4.6: sections 5 to 7 of the content <see cref="CustomerIncomeSummaryReport.Build"/>
/// gives for a tax year read.</summary>
public class CustomerIncomeSummaryReportDetailTests
{
    [Test]
    public void TheWorkedExample_HasTheSevenSectionsInOrder() =>
        Build(Example).Sections.Select(section => section.Title).ShouldBe([
            "1. Summary",
            "2. About this report",
            "3. Dividends",
            "4. Interest",
            "5. Each payment",
            "6. Exchange rates used",
            "7. What to know about these figures"
        ]);

    [Test]
    public void TheWorkedExample_EachPayment()
    {
        var section = Build(Example).Section(5);

        section.Table(0).Columns.ShouldBe([
            "Time", "Symbol", "Account", "Custody", "Shares", "Tax source", "Gross", "Rate", "Dividend", "Imputation credit", "Foreign tax",
            "RWT", "NRWT"
        ]);
        section.Table(1).Columns.ShouldBe(["Time", "Account", "Custody", "Period", "Gross", "Rate", "Interest", "RWT", "NRWT", "Foreign tax"]);
        section.Lines().ShouldBe([
            "# Dividends",
            "2025-12-12 13:00 | KIW | Old broker | Held elsewhere | 1000 | New Zealand | 720.00 NZD | no conversion | 720.00 | 280.00 | 0.00 | 50.00 | 0.00",
            "2026-03-20 13:00 | OZB | Old broker | Held elsewhere | 200 | Foreign | 327.00 NZD | no conversion | 327.00 | 0.00 | 49.05 | 0.00 | 0.00",
            "2025-05-20 14:10 | AAA | Trading | Held on platform | 70 | Foreign | 35.00 USD | x 1.69 (2025-05-20) | 59.15 | 0.00 | 8.87 | 10.65 | 0.00",
            "2025-09-15 14:30 | AAA | Trading | Held on platform | 140 | Foreign | 35.00 USD | x 1.68 (2025-09-15) | 58.80 | 0.00 | 8.82 | 10.58 | 0.00",
            "# Interest",
            "2025-09-30 13:00 | Old broker | Held elsewhere | entered by hand | 120.00 NZD | no conversion | 120.00 | 21.00 | 0.00 | 0.00",
            "2025-10-15 13:00 | Old broker | Held elsewhere | entered by hand | -8.00 NZD | no conversion | -8.00, charged | 0.00 | 0.00 | 0.00",
            "2026-02-01 13:30 | Trading | Held on platform | 2026-01-01 to 2026-01-31, 31 days | 4.10 USD | x 1.72 (2026-01-30) | 7.05 | 2.32 | 0.00 | 0.00",
            "2026-03-01 13:30 | Trading | Held on platform | 2026-02-01 to 2026-02-28, 28 days | 3.80 USD | x 1.75 (2026-02-27) | 6.65 | 2.19 | 0.00 | 0.00"
        ]);
    }

    [Test]
    public void APaymentWithNoRateOrAStaleRate_IsNotAvailableWithTheLabelOfItsStatus()
    {
        var stale = Rate(2025, 5, 14, 1.7000m);

        var lines = Build(Year([T4, I3], rates: [stale])).Section(5).Lines();

        lines.ShouldBe([
            "# Dividends",
            "2025-05-20 14:10 | AAA | Trading | Held on platform | 70 | Foreign | 35.00 USD | x 1.7 (2025-05-14) | not available (Exchange rate too old) | not available (Exchange rate too old) | not available (Exchange rate too old) | not available (Exchange rate too old) | not available (Exchange rate too old)",
            "# Interest",
            "2026-02-01 13:30 | Trading | Held on platform | 2026-01-01 to 2026-01-31, 31 days | 4.10 USD | x 1.7 (2025-05-14) | not available (Exchange rate too old) | not available (Exchange rate too old) | not available (Exchange rate too old) | not available (Exchange rate too old)"
        ]);
        Build(Year([I3], rates: [])).Section(5).Lines()[1].ShouldBe(
            "2026-02-01 13:30 | Trading | Held on platform | 2026-01-01 to 2026-01-31, 31 days | 4.10 USD | not available | not available (No exchange rate) | not available (No exchange rate) | not available (No exchange rate) | not available (No exchange rate)");
    }

    [Test]
    public void WithOnlyInterest_EachPaymentHasNoDividendPart() =>
        Build(Year([I1])).Section(5).Lines().ShouldBe([
            "# Interest",
            "2025-09-30 13:00 | Old broker | Held elsewhere | entered by hand | 120.00 NZD | no conversion | 120.00 | 21.00 | 0.00 | 0.00"
        ]);

    [Test]
    public void TheWorkedExample_ExchangeRatesUsed()
    {
        var section = Build(Example).Section(6);

        section.Table(0).Columns.ShouldBe(["Pair", "Date", "Rate", "Source"]);
        section.Lines().ShouldBe([
            "USD to NZD | 2025-05-20 | 1.69 | Frankfurter",
            "USD to NZD | 2025-09-15 | 1.68 | Frankfurter",
            "USD to NZD | 2026-01-30 | 1.72 | Frankfurter",
            "USD to NZD | 2026-02-27 | 1.75 | Frankfurter",
            "Ledger read at 2026-11-28 16:12"
        ]);
    }

    [Test]
    public void ARateUsedByTwoPayments_IsListedOnce()
    {
        var second = Posting(150, Utc(2026, 2, 1, 5, 0), 1.00m, 0m, null);

        Build(Year([I3, second])).Section(6).Table(0).Rows.ShouldBe([["USD to NZD", "2026-01-30", "1.72", "Frankfurter"]]);
    }

    [Test]
    public void EveryPaymentInNzd_HasNoRate() =>
        Build(Year([I1, D1])).Section(6).Lines().ShouldBe([
            "None. Every payment was booked in New Zealand dollars.",
            "Ledger read at 2026-11-28 16:12"
        ]);

    [Test]
    public void TheWorkedExample_WhatToKnow()
    {
        Build(Example).Section(7).Lines().ShouldBe([
            "# What these figures rest on",
            "Old broker: The payments of this account were recorded by your adviser from outside statements. The platform has not checked them.",
            "Old broker: Interest was charged to this account. It is listed in section 4 and is in no total of interest received."
        ]);
    }

    [Test]
    public void AnIncompleteReport_SaysWhatIsMissing()
    {
        var treatments = new Dictionary<int, TaxTreatment> { [Kiw] = new(FifTreatment.NotForeign, 2025, null, "NZ") };

        Build(Year([T4, D1, I3], rates: [], treatments: treatments)).Section(7).Lines().ShouldBe([
            "# What is missing",
            "AAA: The dividend of 2025-05-20 14:10 could not be converted to New Zealand dollars (No exchange rate). It is in no total.",
            "AAA: Not classified for tax year 2026. Its dividends are in a group of their own.",
            "Trading: The interest of 2026-02-01 13:30 could not be converted to New Zealand dollars (No exchange rate). It is in no total.",
            "# What these figures rest on",
            "Old broker: The payments of this account were recorded by your adviser from outside statements. The platform has not checked them."
        ]);
    }

    [Test]
    public void TheNoticesOfAHolding_HaveTheirSentences()
    {
        var treatments = new Dictionary<int, TaxTreatment>
        {
            [Aaa] = new(FifTreatment.Attributing, 2025, null, "US"),
            [Kiw] = new(FifTreatment.NotForeign, 2025, null, "AU")
        };
        var scrip = Fact(160, Utc(2025, 9, 2, 0, 0), TransactionType.Scrip, [], [Security(160, A1, 4m, 0m)]);

        Build(Year([T1, T4, scrip, D1], treatments: treatments)).Section(7).Lines().ShouldBe([
            "# What these figures rest on",
            "Old broker: The payments of this account were recorded by your adviser from outside statements. The platform has not checked them.",
            "KIW: A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded.",
            "AAA: Shares were issued in place of a dividend. They are not in this report. The platform cannot know whether the issue was a taxable dividend."
        ]);
    }

    [Test]
    public void WithNothingToKnow_TheSectionSaysSo() =>
        Build(Year([T4, I3])).Section(7).Lines().ShouldBe(["Nothing to report."]);
}
