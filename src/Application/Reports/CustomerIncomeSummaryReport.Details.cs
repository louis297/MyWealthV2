using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;
using static MyWealthV2.Application.Reports.TaxReportFormat;

namespace MyWealthV2.Application.Reports;

/// <summary>Sections 5 to 7 of the Tax Year Income Summary (docs/features/income-summary.md R29 to R31, §4.6).</summary>
public static partial class CustomerIncomeSummaryReport
{
    private sealed partial class Report
    {
        private List<IncomeSummarySection> Details() => [EachPayment(), RatesUsed(), WhatToKnow()];

        /// <summary>R29: the dividends in the order of the groups, the holdings and the events; then the interest items.</summary>
        private List<(TaxHolding Holding, TaxYearEvent Event)> DividendPayments() =>
            Income.Dividends.Groups
                .SelectMany(group => Ordered(group.Holdings))
                .Select(row => Holding(row.InstrumentId))
                .SelectMany(holding => holding.Events.Where(e => e.Dividend is not null).Select(e => (holding, e)))
                .ToList();

        private IncomeSummarySection EachPayment()
        {
            var blocks = new List<IncomeSummaryBlock>();
            var dividends = DividendPayments();
            if (dividends.Count > 0)
            {
                blocks.Add(new IncomeSummaryText("Dividends", Heading: true));
                blocks.Add(new IncomeSummaryTable(
                    [
                        "Time", "Symbol", "Account", "Custody", "Shares", "Tax source", "Gross", "Rate", "Dividend", "Imputation credit",
                        "Foreign tax", "RWT", "NRWT"
                    ],
                    dividends.Select(payment => DividendRow(payment.Holding, payment.Event)).ToList()));
            }

            if (Year.Interest.Count > 0)
            {
                blocks.Add(new IncomeSummaryText("Interest", Heading: true));
                blocks.Add(new IncomeSummaryTable(
                    ["Time", "Account", "Custody", "Period", "Gross", "Rate", "Interest", "RWT", "NRWT", "Foreign tax"],
                    Year.Interest.Select(InterestRow).ToList()));
            }

            return new IncomeSummarySection("5. Each payment", blocks);
        }

        private IReadOnlyList<string> DividendRow(TaxHolding holding, TaxYearEvent payment)
        {
            var booked = payment.Event.Dividend!;
            var nzd = payment.Dividend!;
            string Figure(decimal? amount) => Missing(amount, payment.Status);
            return
            [
                Time(payment.Event.BookedAt),
                Instrument(holding.InstrumentId).Symbol,
                Account(payment.Event.AccountId),
                CustodyLabel(payment.Event.CustodyMode),
                Number(booked.Shares),
                booked.TaxSource == DividendTaxSource.NewZealand ? "New Zealand" : "Foreign",
                Money(booked.Gross, booked.Currency),
                Rate(payment.Status, payment.Fx),
                Figure(nzd.Gross),
                Figure(nzd.ImputationCredit),
                Figure(nzd.ForeignTax),
                Figure(nzd.Rwt),
                Figure(nzd.Nrwt)
            ];
        }

        private IReadOnlyList<string> InterestRow(TaxYearInterest item)
        {
            var booked = item.Item;
            string Figure(decimal? amount) => Missing(amount, item.Status);
            var amount = Figure(item.Amount);
            return
            [
                Time(booked.BookedAt),
                Account(booked.AccountId),
                CustodyLabel(booked.CustodyMode),
                booked.Period is { } period
                    ? $"{Date(period.PeriodFrom)} to {Date(period.PeriodTo)}, {period.Days} days"
                    : "entered by hand",
                Money(booked.Gross, booked.Currency),
                Rate(item.Status, item.Fx),
                booked.Gross < 0m ? amount + ", charged" : amount,
                Figure(item.Rwt),
                Figure(item.Nrwt),
                Figure(item.ForeignTax)
            ];
        }

        /// <summary>R29: the rate with its date, "no conversion" for a figure booked in NZD, or "not available".</summary>
        private static string Rate(TaxAmountStatus status, StoredRate? fx) =>
            fx is { } rate ? RateText(rate) : status == TaxAmountStatus.Stated ? "no conversion" : NotAvailable;

        /// <summary>R29: an NZD figure, or "not available ({label of its status})".</summary>
        private string Missing(decimal? amount, TaxAmountStatus status) =>
            amount is null ? $"{NotAvailable} ({StatusLabel(status)})" : Nzd(amount);

        /// <summary>R30: every rate row behind a payment, once each, by pair and then by date.</summary>
        private IncomeSummarySection RatesUsed()
        {
            var dividends = DividendPayments();
            var rows = dividends
                .Where(payment => payment.Event.Fx is not null)
                .Select(payment => (payment.Event.Event.Dividend!.Currency, Rate: payment.Event.Fx!))
                .Concat(Year.Interest.Where(item => item.Fx is not null).Select(item => (item.Item.Currency, Rate: item.Fx!)))
                .Distinct()
                .OrderBy(row => row.Currency, StringComparer.Ordinal)
                .ThenBy(row => row.Rate.AsOfDate)
                .Select(row => (IReadOnlyList<string>)[$"{row.Currency} to NZD", Date(row.Rate.AsOfDate), Number(row.Rate.Rate), row.Rate.Source])
                .ToList();

            var blocks = new List<IncomeSummaryBlock>();
            if (rows.Count > 0)
            {
                blocks.Add(new IncomeSummaryTable(["Pair", "Date", "Rate", "Source"], rows));
            }
            else
            {
                var allStated = dividends.All(payment => payment.Event.Status == TaxAmountStatus.Stated)
                                && Year.Interest.All(item => item.Status == TaxAmountStatus.Stated);
                blocks.Add(Text(allStated ? "None. Every payment was booked in New Zealand dollars." : "None."));
            }

            blocks.Add(Text($"Ledger read at {Time(ledgerReadAt)}"));
            return new IncomeSummarySection("6. Exchange rates used", blocks);
        }

        /// <summary>R31, §4.6: what is missing, then what the figures rest on.</summary>
        private IncomeSummarySection WhatToKnow()
        {
            var missing = new List<string>();
            foreach (var (holding, payment) in DividendPayments().Where(row => row.Event.Dividend!.Gross is null))
            {
                missing.Add(
                    $"{Instrument(holding.InstrumentId).Symbol}: The dividend of {Time(payment.Event.BookedAt)} could not be converted to New Zealand dollars ({StatusLabel(payment.Status)}). It is in no total.");
            }

            foreach (var reason in Income.Incomplete.Where(reason => reason.Reason == IncomeIncompleteReason.NotClassified))
            {
                missing.Add($"{Instrument(reason.InstrumentId!.Value).Symbol}: Not classified for tax year {TaxYear}. Its dividends are in a group of their own.");
            }

            foreach (var item in Year.Interest.Where(item => item.Amount is null))
            {
                missing.Add(
                    $"{Account(item.Item.AccountId)}: The interest of {Time(item.Item.BookedAt)} could not be converted to New Zealand dollars ({StatusLabel(item.Status)}). It is in no total.");
            }

            var notices = Income.Notices.Select(NoticeSentence).ToList();
            var blocks = new List<IncomeSummaryBlock>();
            if (missing.Count > 0)
            {
                blocks.Add(new IncomeSummaryText("What is missing", Heading: true));
                blocks.AddRange(missing.Select(Text));
            }

            if (notices.Count > 0)
            {
                blocks.Add(new IncomeSummaryText("What these figures rest on", Heading: true));
                blocks.AddRange(notices.Select(Text));
            }

            if (blocks.Count == 0)
            {
                blocks.Add(Text("Nothing to report."));
            }

            return new IncomeSummarySection("7. What to know about these figures", blocks);
        }

        private string NoticeSentence(IncomeNoticeLine line) => line.Notice switch
        {
            IncomeNotice.ExternalFigures =>
                $"{Account(line.AccountId!.Value)}: The payments of this account were recorded by your adviser from outside statements. The platform has not checked them.",
            IncomeNotice.DividendTaxSourceDisagrees =>
                $"{Instrument(line.InstrumentId!.Value).Symbol}: A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded.",
            IncomeNotice.Scrip =>
                $"{Instrument(line.InstrumentId!.Value).Symbol}: Shares were issued in place of a dividend. They are not in this report. The platform cannot know whether the issue was a taxable dividend.",
            _ =>
                $"{Account(line.AccountId!.Value)}: Interest was charged to this account. It is listed in section 4 and is in no total of interest received."
        };
    }
}
