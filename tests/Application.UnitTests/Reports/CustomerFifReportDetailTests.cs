using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using NUnit.Framework;
using Shouldly;
using static MyWealthV2.Application.UnitTests.Reports.FifReportFixture;

namespace MyWealthV2.Application.UnitTests.Reports;

/// <summary>Fif-report R21 to R24, §4.6, §4.7: sections 6 to 9 of the content of <see cref="CustomerFifReport.Build"/>.</summary>
public class CustomerFifReportDetailTests
{
    private static readonly DateTimeOffset Noon = new(2025, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void TheWorkedExample_TheBlockOfAaa_OpeningAndClosing()
    {
        var section = Build(Example).Section(6);

        section.Texts().Take(5).ShouldBe([
            "AAA Alpha Inc",
            "United States of America  Attributing interest  classified from tax year 2025",
            "Opening, valued as of 2025-03-31",
            "Total 140 (280 on the basis of the year's end)  13108.48",
            "Closing, valued as of 2026-03-31"
        ]);
        section.Blocks[0].ShouldBe(new CustomerFifText("AAA Alpha Inc", Heading: true));
        var tables = section.Tables();
        tables.Count.ShouldBe(4);
        tables[0].Columns.ShouldBe(["Account", "Custody", "Quantity", "Price", "Market value", "Rate", "Value"]);
        tables[0].Rows.ShouldBe([
            ["Trading", "Held on platform", "100", "53.20 USD (2025-03-31)", "5320.00 USD", "x 1.76 (2025-03-31)", "9363.20"],
            ["Old broker", "Held elsewhere", "40", "53.20 USD (2025-03-31)", "2128.00 USD", "x 1.76 (2025-03-31)", "3745.28"]
        ]);
        tables[1].Rows.ShouldBe([
            ["Trading", "Held on platform", "240.8", "31.00 USD (2026-03-31)", "7464.80 USD", "x 1.745 (2026-03-31)", "13026.08"]
        ]);
        section.Texts().ShouldContain("Total 240.8  13026.08");
    }

    [Test]
    public void TheWorkedExample_TheBlockOfAaa_Events()
    {
        var events = Build(Example).Section(6).Table(2);

        events.Columns.ShouldBe(["#", "Time", "Event", "Account", "Quantity", "Quantity after", "Comparable after", "Amount", "Rate", "Cost change", "Tax cost after"]);
        events.Rows.ShouldBe([
            ["1", "2025-04-01 09:15", "Sale", "Trading", "-30", "110", "220", "2789.60 (2798.40 less a fee of 8.80)", "x 1.76 (2025-03-31)", "-2647.65", "9708.05"],
            ["2", "2025-05-20 14:10", "Dividend", "Trading", "0", "110", "220", "59.15", "x 1.69 (2025-05-20)", "", "9708.05"],
            ["3", "2025-07-01 12:00", "Share reorganisation", "Old broker", "+40", "150", "220", "", "", "", "9708.05"],
            ["4", "2025-07-03 00:01", "Share reorganisation", "Trading", "+70", "220", "220", "", "", "", "9708.05"],
            ["5", "2025-09-15 14:30", "Dividend", "Trading", "0", "220", "220", "58.80", "x 1.68 (2025-09-15)", "", "9708.05"],
            ["6", "2025-09-15 14:30", "Purchase", "Trading", "+0.8", "220.8", "220.8", "37.63", "x 1.68 (2025-09-15)", "+37.63", "9745.68"],
            ["7", "2025-11-14 16:20", "Moved between your accounts", "Trading", "0", "220.8", "220.8", "80 from Old broker (2025-11-10 13:00) to Trading", "", "", "9745.68"],
            ["8", "2026-02-03 10:40", "Purchase", "Trading", "+20", "240.8", "240.8", "1046.65 (1038.00 and a fee of 8.65)", "x 1.73 (2026-02-02)", "+1046.65", "10792.33"]
        ]);
    }

    [Test]
    public void TheWorkedExample_TheBlockOfAaa_DividendsTaxCostAndIncome()
    {
        var section = Build(Example).Section(6);

        var dividends = section.Table(3);
        dividends.Columns.ShouldBe(["#", "Shares", "Gross", "Foreign tax", "RWT", "NRWT", "Imputation credit", "Tax source"]);
        dividends.Rows.ShouldBe([
            ["2", "70", "59.15", "8.87", "10.65", "0.00", "0.00", "Foreign"],
            ["5", "140", "58.80", "8.82", "10.58", "0.00", "0.00", "Foreign"]
        ]);
        section.Texts().Skip(6).ShouldBe([
            "Events",
            "Dividends",
            "Tax cost: 12355.70 at the start, 10792.33 at the end",
            "Fair dividend rate: 0.05 x 13108.48 = 655.42",
            "Quick sale: opening 280, peak 280, closing 240.8; peak holding differential 0",
            "Purchases of the year: 20.8 shares for 1084.28, average cost 52.12884615",
            "Peak holding method amount 0.00    Shares matched 0    Quick sale gain amount 0.00",
            "Comparative value: (13026.08 + 2789.60 + 117.95) - (13108.48 + 1084.28) = 1740.87",
            "Notices: Held elsewhere; Moved between your accounts. See section 9."
        ]);
    }

    [Test]
    public void AQuickSaleWithMatches_PrintsEachMatchSaleAndDividendShare()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var quickSale = new QuickSale(
            200m, 260m, 180m, 20m, 80m, 4_000.00m, 50m, 50.00m, 40m, 2_400.00m, 12.00m, 2_000.00m, 412.00m,
            [new QuickSaleMatch(6, 8, 40m)],
            [new QuickSaleSale(8, 60m, 40m, 3_600.00m, 2_400.00m)],
            [new QuickSaleDividendShare(5, 30.00m, 100m, 40m, 12.00m)]);
        year = year with { Holdings = [aaa with { Income = aaa.Income! with { QuickSale = quickSale } }] };

        var texts = Build(year).Section(6).Texts();

        var start = texts.IndexOf("Quick sale: opening 200, peak 260, closing 180; peak holding differential 20");
        start.ShouldBeGreaterThan(0);
        texts.Skip(start).Take(7).ShouldBe([
            "Quick sale: opening 200, peak 260, closing 180; peak holding differential 20",
            "Purchases of the year: 80 shares for 4000.00, average cost 50",
            "Peak holding method amount 50.00    Shares matched 40    Quick sale gain amount 412.00",
            "40 of purchase 6 sold in sale 8",
            "Sale 8: 40 of 60 shares matched, 2400.00 of its proceeds of 3600.00",
            "Dividend 5: 12.00 of 30.00, for 40 of 100 shares held",
            "Proceeds of the shares matched 2400.00    Their dividends 12.00    At the average cost 2000.00"
        ]);
    }

    [Test]
    public void NoPurchaseOrNoSale_NoQuickSale()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var quickSale = aaa.Income!.QuickSale! with { Sales = [] };
        year = year with { Holdings = [aaa with { Income = aaa.Income with { QuickSale = quickSale } }] };

        Build(year).Section(6).Texts().ShouldContain("No quick sale in this tax year.");
    }

    [Test]
    public void AHoldingNotComputed_HasTheSentenceOfItsReasonAndNoFigureOfEitherMethod()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var sss = aaa.As(Sss, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "US"),
            new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null));
        var nnn = aaa.As(Nnn, TaxTreatment.NotClassified(), new TaxHoldingIncome(Nnn, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified, null, null, null))
            with { Incomplete = [TaxIncompleteReason.NotClassified] };
        year = year with { Holdings = [aaa, sss, nnn] };

        var section = Build(year).Section(6);

        section.Blocks.OfType<CustomerFifText>().Where(text => text.Heading).Select(text => text.Text)
            .ShouldBe(["AAA Alpha Inc", "SSS Sierra Fund", "NNN November Ltd"]);
        var texts = section.Texts();
        texts.ShouldContain("The fair dividend rate method is not allowed for this holding. Its income is not computed here and is in no total. It is worked out under the comparative value method on its own, or the deemed rate of return.");
        texts.ShouldContain("Not classified for tax year 2026. It may be an attributing interest. It is in no total, the threshold test cannot be completed, and FIF income totals are not given.");
        texts.Count(text => text.StartsWith("Fair dividend rate:", StringComparison.Ordinal)).ShouldBe(1);
        texts.Count(text => text.StartsWith("Comparative value:", StringComparison.Ordinal)).ShouldBe(1);
        texts.ShouldContain("Missing: Not classified. See section 9.");
    }

    [Test]
    public void NothingHeldAndNoEvents_AreSaid()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var opening = aaa.Opening with { Quantity = 0m, ComparableQuantity = 0m, Value = 0m, Lines = [] };
        year = year with { Holdings = [aaa with { Opening = opening, Events = [] }] };

        var texts = Build(year).Section(6).Texts();

        texts.ShouldContain("Nothing held.");
        texts.ShouldContain("No events in this tax year.");
        texts.ShouldNotContain("Dividends");
    }

    [Test]
    public void ALineNotValued_AndAnEventWithNoAmount_ShowTheLabelOfTheStatusAndNotAvailable()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var line = aaa.Closing.Lines[0];
        var notValued = line with
        {
            Line = line.Line with { Status = ValuationLineStatus.StalePrice, Price = null, MarketValue = null, Value = null }
        };
        var closing = aaa.Closing with { Value = null, Status = TaxValueStatus.NotValued, Lines = [notValued] };
        var events = aaa.Events.Select(e => e.Sequence == 8
            ? e with { Amount = null, Status = TaxAmountStatus.NoRate, Fx = null, Principal = null, Fee = null, Cost = e.Cost! with { Change = null, After = null } }
            : e).ToList();
        year = year with { Holdings = [aaa with { Closing = closing, Events = events }] };

        var section = Build(year).Section(6);

        section.Table(1).Rows.ShouldBe([
            ["Trading", "Held on platform", "240.8", "Price too old", "not available", "x 1.745 (2026-03-31)", "not available"]
        ]);
        section.Texts().ShouldContain("Total 240.8  not available");
        section.Table(2).Rows[7].ShouldBe(
            ["8", "2026-02-03 10:40", "Purchase", "Trading", "+20", "240.8", "240.8", "not available (No exchange rate)", "", "not known", "not known"]);
    }

    [Test]
    public void AnArrival_PrintsItsPriceAndItsCarriedCost()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var sale = aaa.Events[0];
        var arrival = sale with
        {
            Event = sale.Event with
            {
                Kind = TaxEventKind.Arrival,
                Quantity = 30m,
                Trade = null,
                CarriedCost = new TaxCarriedCost(1_500.00m, "USD", new DateOnly(2019, 6, 3))
            },
            Amount = 2_808.96m,
            Principal = null,
            Fee = null,
            Price = new StoredPrice(53.20m, "USD", new DateOnly(2025, 3, 31), "EODHD"),
            Cost = new TaxEventCost(2_610.00m, 14_965.70m, new StoredRate(1.74m, new DateOnly(2019, 6, 3), "Frankfurter"))
        };
        year = year with { Holdings = [aaa with { Events = [arrival, .. aaa.Events.Skip(1)] }] };

        var content = Build(year);

        content.Section(6).Table(2).Rows[0][7].ShouldBe(
            "2808.96 at 53.20 USD (2025-03-31); carried cost 1500.00 USD x 1.74 (2019-06-03), acquired on 2019-06-03");
        content.Section(8).Table(1).Rows.ShouldContain(["USD to NZD", "2019-06-03", "1.74", "Frankfurter"]);
    }

    [Test]
    public void TheWorkedExample_TaxTakenFromDividends()
    {
        var section = Build(Example).Section(7);

        var tables = section.Tables();
        tables.Count.ShouldBe(2);
        tables[0].Columns.ShouldBe(["Symbol", "Issuer country", "Dividends", "Foreign tax", "RWT", "NRWT", "Imputation credits", "FDR amount", "CV amount"]);
        tables[0].Rows.ShouldBe([["AAA", "United States of America", "117.95", "17.69", "21.23", "0.00", "0.00", "655.42", "1740.87"]]);
        tables[1].Columns.ShouldBe(["Country", "Dividends", "Foreign tax", "RWT", "NRWT", "Imputation credits"]);
        tables[1].Rows.ShouldBe([
            ["United States of America", "117.95", "17.69", "21.23", "0.00", "0.00"],
            ["Total", "117.95", "17.69", "21.23", "0.00", "0.00"]
        ]);
        section.Texts().ShouldBe([
            "Foreign tax taken from a dividend may give a foreign tax credit. The credit is limited, and this report does not work it out. Resident withholding tax (RWT) is offset against the tax you owe. See Inland Revenue's guide to foreign investment funds (IR461)."
        ]);
    }

    [Test]
    public void TaxTaken_PerCountryInTheOrderOfTheirNames_CountryNotStatedLast_AndADividendWithNoRate()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var nnn = aaa.As(Nnn, TaxTreatment.NotClassified(), new TaxHoldingIncome(Nnn, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified, null, null, null));
        var gb = aaa.As(Bbb, new TaxTreatment(FifTreatment.Attributing, 2025, null, "GB"), aaa.Income! with { InstrumentId = Bbb });
        var sss = aaa.As(Sss, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "GB"),
            new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null));
        var noRate = sss with
        {
            Events = sss.Events.Select(e => e.Sequence == 2
                ? e with { Amount = null, Status = TaxAmountStatus.NoRate, Dividend = e.Dividend! with { Gross = null, ForeignTax = null, Rwt = null } }
                : e).ToList()
        };
        year = year with { Holdings = [aaa, gb, noRate, nnn] };

        var tables = Build(year).Section(7).Tables();
        tables.Count.ShouldBe(2);

        tables[0].Rows.ShouldBe([
            ["AAA", "United States of America", "117.95", "17.69", "21.23", "0.00", "0.00", "655.42", "1740.87"],
            ["BBB", "United Kingdom of Great Britain and Northern Ireland", "117.95", "17.69", "21.23", "0.00", "0.00", "655.42", "1740.87"],
            ["NNN", "not stated", "117.95", "17.69", "21.23", "0.00", "0.00", "not computed", "not computed"],
            ["SSS", "United Kingdom of Great Britain and Northern Ireland", "not available", "not available", "not available", "0.00", "0.00", "not computed", "not computed"]
        ]);
        tables[1].Rows.ShouldBe([
            ["United Kingdom of Great Britain and Northern Ireland", "not available", "not available", "not available", "0.00", "0.00"],
            ["United States of America", "117.95", "17.69", "21.23", "0.00", "0.00"],
            ["Country not stated", "117.95", "17.69", "21.23", "0.00", "0.00"],
            ["Total", "not available", "not available", "not available", "0.00", "0.00"]
        ]);
    }

    [Test]
    public void NoDividend_IsSaid()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with { Holdings = [aaa with { Events = aaa.Events.Where(e => e.Event.Kind != TaxEventKind.Dividend).ToList() }] };

        var section = Build(year).Section(7);

        section.Blocks.ShouldBe([new CustomerFifText("No dividends on these holdings in this tax year.")]);
    }

    [Test]
    public void TheWorkedExample_PricesRatesAndValuesUsed_EachOnce()
    {
        var section = Build(Example).Section(8);

        var tables = section.Tables();
        tables.Count.ShouldBe(2);
        tables[0].Columns.ShouldBe(["Symbol", "Date", "Price", "Source"]);
        tables[0].Rows.ShouldBe([
            ["AAA", "2025-03-31", "53.20 USD", "EODHD"],
            ["AAA", "2026-03-31", "31.00 USD", "EODHD"]
        ]);
        tables[1].Columns.ShouldBe(["Pair", "Date", "Rate", "Source"]);
        tables[1].Rows.ShouldBe([
            ["USD to NZD", "2025-03-31", "1.76", "Frankfurter"],
            ["USD to NZD", "2025-05-20", "1.69", "Frankfurter"],
            ["USD to NZD", "2025-09-15", "1.68", "Frankfurter"],
            ["USD to NZD", "2026-02-02", "1.73", "Frankfurter"],
            ["USD to NZD", "2026-03-31", "1.745", "Frankfurter"]
        ]);
        section.Texts().ShouldBe([
            "Prices",
            "Rates",
            "FIF threshold of tax year 2026: 50000.00    Fair dividend rate of tax year 2026: 0.05",
            "Ledger read at 2026-11-28 16:12",
            "The tax cost at the start of the tax year comes from purchases of earlier tax years. Their rates are not listed here."
        ]);
    }

    [Test]
    public void NoThreshold_IsNotSetInSection8()
    {
        var year = Example with { TaxYear = 2027, Threshold = Example.Threshold with { Amount = null } };

        Build(year).Section(8).Texts().ShouldContain("FIF threshold of tax year 2027: not set    Fair dividend rate of tax year 2027: 0.05");
    }

    [Test]
    public void TheWorkedExample_WhatToKnow_TheNoticesOnly()
    {
        Build(Example).Section(9).Blocks.ShouldBe([
            new CustomerFifText("What these figures rest on", Heading: true),
            new CustomerFifText("AAA: Some of these figures come from an account held elsewhere. They were recorded by your adviser from outside statements, and the platform has not checked them."),
            new CustomerFifText("AAA: A transfer out of one of your accounts and a transfer into another were read as one move, not as a sale and a purchase. Nothing in the ledger links the two entries.")
        ]);
    }

    [Test]
    public void NoReasonAndNoNotice_NothingToReport()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with { Holdings = [aaa with { Notices = [] }] };

        Build(year).Section(9).Blocks.ShouldBe([new CustomerFifText("Nothing to report.")]);
    }

    [Test]
    public void EveryNotice_GivesItsSentence()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with { Holdings = [aaa with { Notices = Enum.GetValues<TaxNotice>() }] };

        Build(year).Section(9).Texts().ShouldBe([
            "What these figures rest on",
            "AAA: Some of these figures come from an account held elsewhere. They were recorded by your adviser from outside statements, and the platform has not checked them.",
            "AAA: Shares were transferred in. They are counted as bought at the market value of that day, because the platform does not know when they were first acquired.",
            "AAA: Shares were transferred out. They are counted as sold at the market value of that day, because the platform does not know what became of them.",
            "AAA: A transfer out of one of your accounts and a transfer into another were read as one move, not as a sale and a purchase. Nothing in the ledger links the two entries.",
            "AAA: Shares were issued in place of a dividend. They are read as a share reorganisation. The platform cannot know whether the issue was a taxable dividend.",
            "AAA: A correction changed this holding. It is not counted as a purchase or a sale.",
            "AAA: A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded.",
            "AAA: The cost of shares that were transferred in is the cost stated at the transfer, converted at one exchange rate."
        ]);
    }

    [Test]
    public void EveryReason_GivesItsSentence_NotClassifiedOnce_AndHoldingIncompleteNotBesideTheHoldingsOwn()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        var openingLine = aaa.Opening.Lines[0];
        var holding = aaa with
        {
            Notices = [],
            Incomplete =
            [
                TaxIncompleteReason.OpeningNotValued, TaxIncompleteReason.ClosingNotValued, TaxIncompleteReason.AmountMissing,
                TaxIncompleteReason.NegativeQuantity, TaxIncompleteReason.NoComparableQuantity
            ],
            Opening = aaa.Opening with { Lines = [openingLine with { Line = openingLine.Line with { Status = ValuationLineStatus.NoPrice } }] },
            Closing = aaa.Closing with { Lines = [aaa.Closing.Lines[0] with { Line = aaa.Closing.Lines[0].Line with { Status = ValuationLineStatus.NoRate } }] },
            Events = aaa.Events.Select(e => e.Sequence switch
            {
                2 => e with { Amount = null, Status = TaxAmountStatus.StaleRate },
                8 => e with { Amount = null, Status = TaxAmountStatus.NoRate },
                _ => e
            }).ToList(),
            Cost = new TaxCost(TaxCostStatus.Unknown, null, null,
                new TaxCostUnknown(TaxCostUnknownCause.NegativeCost, 4, Noon, Guid.Empty)),
            Income = new TaxHoldingIncome(Aaa, FifIncomeStatus.NotComputed, FifIncomeReason.HoldingIncomplete, null, null, null)
        };
        var nnn = aaa.As(Nnn, TaxTreatment.NotClassified(), new TaxHoldingIncome(Nnn, FifIncomeStatus.NotComputed, FifIncomeReason.NotClassified, null, null, null))
            with { Incomplete = [TaxIncompleteReason.NotClassified], Notices = [] };
        var sss = aaa.As(Sss, new TaxTreatment(FifTreatment.AttributingNoFdr, 2025, null, "US"),
            new TaxHoldingIncome(Sss, FifIncomeStatus.NotComputed, FifIncomeReason.FdrNotAllowed, null, null, null)) with { Notices = [] };
        year = year with
        {
            TaxYear = 2027,
            Complete = false,
            Holdings = [holding, nnn, sss],
            Threshold = year.Threshold with
            {
                Amount = null,
                Outcome = ThresholdOutcome.Unknown,
                Incomplete =
                [
                    new ThresholdIncomplete(ThresholdIncompleteReason.NoThreshold, null),
                    new ThresholdIncomplete(ThresholdIncompleteReason.NotClassified, Nnn),
                    new ThresholdIncomplete(ThresholdIncompleteReason.CostUnknown, Aaa),
                    new ThresholdIncomplete(ThresholdIncompleteReason.TreatmentChanged, Sss)
                ]
            },
            FifIncome = year.FifIncome with
            {
                Fdr = null,
                Cv = null,
                Lower = null,
                Complete = false,
                Incomplete =
                [
                    new FifIncomeIncomplete(FifIncomeReason.HoldingIncomplete, Aaa),
                    new FifIncomeIncomplete(FifIncomeReason.NotClassified, Nnn),
                    new FifIncomeIncomplete(FifIncomeReason.FdrNotAllowed, Sss)
                ]
            }
        };

        Build(year).Section(9).Texts().ShouldBe([
            "What is missing",
            "No FIF threshold is set on this platform for tax year 2027.",
            "AAA: No market value at the start of the tax year (No price). Its FIF income is not computed.",
            "AAA: No market value at the end of the tax year (No exchange rate). Its FIF income is not computed.",
            "AAA: An amount of event 2 could not be worked out (Exchange rate too old). Its FIF income is not computed.",
            "AAA: An amount of event 8 could not be worked out (No exchange rate). Its FIF income is not computed.",
            "AAA: The quantity is below 0 at some point of the tax year. An entry is missing or was dated too late.",
            "AAA: A share reorganisation was booked on an account that held nothing, so quantities before and after it cannot be compared.",
            "AAA: The cost is not known: a correction left a cost below 0 since 2025-08-01 12:00. It is not in the total of the threshold test.",
            "NNN: Not classified for tax year 2027. It may be an attributing interest. It is in no total, the threshold test cannot be completed, and FIF income totals are not given.",
            "SSS: Its classification changed while you held it. Tax law then starts the cost again from a market value, which this platform does not work out. The cost shown is the cost paid.",
            "SSS: The fair dividend rate method is not allowed for this holding. Its income is not computed here and is in no total. It is worked out under the comparative value method on its own, or the deemed rate of return."
        ]);
    }

    [Test]
    public void ANullFigure_IsNotAvailable_NeverZeroOrEmpty()
    {
        var year = Example;
        var aaa = year.Holdings.Single();
        year = year with
        {
            Holdings =
            [
                aaa with
                {
                    Events = aaa.Events.Select(e => e.Sequence == 5
                        ? e with { Dividend = e.Dividend! with { Rwt = null } }
                        : e).ToList()
                }
            ]
        };

        var content = Build(year);

        content.Section(6).Table(3).Rows[1].ShouldBe(["5", "140", "58.80", "8.82", "not available", "0.00", "0.00", "Foreign"]);
        content.Section(7).Table(0).Rows[0][4].ShouldBe("not available");
        content.Section(7).Table(1).Rows.ShouldAllBe(row => row[3] == "not available");
    }
}
