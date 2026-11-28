using System.Globalization;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using static MyWealthV2.Application.Reports.TaxReportFormat;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of the Tax Year Income Summary from one tax year read (docs/features/income-summary.md §4.4 to §4.6).
/// No I/O and no arithmetic: every figure is a figure of <c>interest</c>, of <c>income</c> or of a <c>Dividend</c>
/// event of the read. The one place the words of the report are chosen.
/// </summary>
public static partial class CustomerIncomeSummaryReport
{
    public const string Title = "Tax Year Income Summary";

    public static CustomerIncomeSummaryContent Build(
        CustomerTaxLedgerRead read,
        CustomerIncomeSummaryNames names,
        TaxResidency residency,
        DateTimeOffset generatedAt,
        DateTimeOffset ledgerReadAt)
    {
        var report = new Report(read, names, residency, generatedAt, ledgerReadAt);
        return new CustomerIncomeSummaryContent(
            Title, report.Header(), report.Sections(), report.Complete, names.CustomerName, read.Year.TaxYear);
    }

    /// <summary>§4.6: the title of each group of R6.</summary>
    private static string GroupTitle(IncomeGroup group) => group switch
    {
        IncomeGroup.NewZealand => "New Zealand companies",
        IncomeGroup.ForeignExempt => "Foreign companies outside the FIF rules",
        IncomeGroup.Attributing => "Attributing interests",
        _ => "Not classified"
    };

    private sealed partial class Report(
        CustomerTaxLedgerRead read,
        CustomerIncomeSummaryNames names,
        TaxResidency residency,
        DateTimeOffset generatedAt,
        DateTimeOffset ledgerReadAt)
    {
        private CustomerTaxYear Year => read.Year;

        private TaxYearIncome Income => read.Year.Income;

        private int TaxYear => read.Year.TaxYear;

        /// <summary>R22: complete when the income summary is.</summary>
        public bool Complete => Income.Complete;

        private bool NothingPaid => Income.Dividends.Total.Count == 0 && Income.Interest.Total.Count == 0;

        private bool Has(IncomeNotice notice) => Income.Notices.Any(line => line.Notice == notice);

        /// <summary>R24.</summary>
        public List<string> Header()
        {
            var y = TaxYear;
            return
            [
                $"Tenant: {names.TenantName}",
                $"Customer: {names.CustomerName}",
                $"Tax year: {y} (1 April {y - 1} to 31 March {y}, New Zealand time)",
                $"Tax year in UTC: {UtcMinute(Year.From)} to {UtcMinute(Year.To)}",
                $"Generated: {Instant(generatedAt)}",
                $"Ledger read at: {Instant(ledgerReadAt)}",
                $"Tax residency on file: {ResidencyLabel(residency)}",
                "All amounts are in New Zealand dollars (NZD). All times are New Zealand time.",
                Complete ? "Status: Complete" : "Status: Incomplete. See section 7."
            ];
        }

        /// <summary>§4.4: the sections in their order; R32: with nothing paid, section 1 is one line and section 2 follows.</summary>
        public List<IncomeSummarySection> Sections()
        {
            if (NothingPaid)
            {
                return
                [
                    new IncomeSummarySection("1. Summary", [Text("No dividends and no interest in this tax year.")]),
                    About()
                ];
            }

            return [Summary(), About(), Dividends(), Interest(), .. Details()];
        }

        /// <summary>R26.</summary>
        private IncomeSummarySection Summary()
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var group in Income.Dividends.Groups.Where(group => group.Total.Count > 0))
            {
                rows.Add([GroupTitle(group.Group), .. DividendCells(group.Total)]);
            }

            var dividends = Income.Dividends.Total;
            rows.Add(["All dividends", .. DividendCells(dividends)]);
            var interest = Income.Interest.Total;
            rows.Add([
                "Interest received", Count(interest.Count), Nzd(interest.Received), "", "", Nzd(interest.ForeignTax), Nzd(interest.Rwt), Nzd(interest.Nrwt)
            ]);
            var taken = Income.TaxWithheld;
            rows.Add([
                "Tax taken and credits", "", "", Nzd(Income.ImputationCredit), "", Nzd(taken.ForeignTax), Nzd(taken.Rwt), Nzd(taken.Nrwt)
            ]);

            var blocks = new List<IncomeSummaryBlock>
            {
                new IncomeSummaryTable(["", "Payments", "Amount", "Imputation credits", "Gross dividends", "Foreign tax", "RWT", "NRWT"], rows)
            };
            if (Has(IncomeNotice.InterestCharged))
            {
                blocks.Add(Text($"Interest charged: {Nzd(interest.Charged)}. It is not taken off the interest received."));
            }

            blocks.Add(Text(
                "RWT and imputation credits are credits against New Zealand tax. Overseas tax paid may give a foreign tax credit. The credit is limited, and this report does not work it out."));
            return new IncomeSummarySection("1. Summary", blocks);
        }

        /// <summary>R25: the statements of §4.4, in order.</summary>
        private IncomeSummarySection About()
        {
            var statements = new List<string>
            {
                "This report is not tax advice. It lists the dividends and interest recorded for you on this platform and the tax taken from them. It does not say what income you return or what tax you owe.",
                residency == TaxResidency.NewZealand
                    ? "Your tax residency on file is New Zealand, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year."
                    : "Your tax residency on file is Overseas, as recorded when this report was made. What this report says about the return is written for a New Zealand tax resident. A person who is not a New Zealand tax resident files a different return.",
                "This report covers the accounts recorded on this platform. Dividends and interest you were paid elsewhere are in it only if your adviser has recorded them here. It lists no other income: no gain on a sale, no rent and no salary."
            };
            if (Has(IncomeNotice.ExternalFigures))
            {
                statements.Add(CustomerTransactionsPdf.ExternalNote);
            }

            statements.Add("A payment is in the tax year in which it reached your account. Interest earned in March and paid in April is in the next tax year.");
            statements.Add("Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 6 lists every rate used.");
            statements.Add("This report is not a form of Inland Revenue and not a withholding tax certificate. The payer of a dividend or of interest may send you a statement of its own.");
            statements.Add("This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a payment is entered, changed in date or reversed after that time, or when a rate is added.");
            return new IncomeSummarySection("2. About this report", statements.Select(Text).ToList<IncomeSummaryBlock>());
        }

        /// <summary>R27, §4.6.</summary>
        private IncomeSummarySection Dividends()
        {
            if (Income.Dividends.Total.Count == 0)
            {
                return new IncomeSummarySection("3. Dividends", [Text("No dividends in this tax year.")]);
            }

            var blocks = new List<IncomeSummaryBlock>();
            foreach (var group in Income.Dividends.Groups)
            {
                blocks.Add(new IncomeSummaryText(GroupTitle(group.Group), Heading: true));
                if (group.Holdings.Count == 0)
                {
                    blocks.Add(Text("None."));
                    continue;
                }

                blocks.Add(Text(GroupSentence(group.Group)));
                var rows = new List<IReadOnlyList<string>>();
                foreach (var row in Ordered(group.Holdings))
                {
                    var instrument = Instrument(row.InstrumentId);
                    var treatment = Holding(row.InstrumentId).Treatment;
                    rows.Add([
                        instrument.Symbol, instrument.Name, CountryName(row.IssuerCountry) ?? "not stated", TreatmentLabel(treatment),
                        .. DividendCells(row.Sum)
                    ]);
                }

                foreach (var country in group.Countries)
                {
                    rows.Add([CountryName(country.IssuerCountry) ?? "Country not stated", "", "", "", .. DividendCells(country.Sum)]);
                }

                rows.Add(["Total", "", "", "", .. DividendCells(group.Total)]);
                blocks.Add(new IncomeSummaryTable(
                    [
                        "Symbol", "Name", "Issuer country", "Classification", "Payments", "Dividends", "Imputation credits", "Gross dividends",
                        "Foreign tax", "RWT", "NRWT"
                    ],
                    rows));
            }

            blocks.Add(Text("Dividends are before any tax was taken. Gross dividends are the dividends with their imputation credits."));
            return new IncomeSummarySection("3. Dividends", blocks);
        }

        private string GroupSentence(IncomeGroup group) => group switch
        {
            IncomeGroup.NewZealand =>
                "Inland Revenue's individual return asks for New Zealand dividends as the gross dividend, with the imputation credits and the RWT.",
            IncomeGroup.ForeignExempt =>
                "These holdings are classified as exempt from the FIF rules. Their dividends are overseas income under the ordinary tax rules. Inland Revenue's overseas income summary (IR1261) asks for overseas income by income type and by country, with the overseas tax paid. RWT is New Zealand tax, not overseas tax.",
            IncomeGroup.Attributing =>
                "If the FIF rules apply to you, these dividends are not returned as dividends: FIF income is returned in their place. If the FIF rules do not apply to you, they are overseas income under the ordinary tax rules, like the dividends of the group above. This report does not say which is the case. The FIF Report of the same tax year shows the threshold test and FIF income.",
            _ =>
                $"These holdings are not classified for tax year {TaxYear}. This report cannot say which of the groups above their dividends belong to. Ask your adviser to have them classified."
        };

        /// <summary>R28, §4.6.</summary>
        private IncomeSummarySection Interest()
        {
            if (Income.Interest.Total.Count == 0)
            {
                return new IncomeSummarySection("4. Interest", [Text("No interest in this tax year.")]);
            }

            var rows = Income.Interest.Accounts
                .Select(row => (IReadOnlyList<string>)
                    [Account(row.AccountId), CustodyLabel(row.CustodyMode), row.Currency, .. InterestCells(row.Sum)])
                .Append(["Total", "", "", .. InterestCells(Income.Interest.Total)])
                .ToList();
            var blocks = new List<IncomeSummaryBlock>
            {
                new IncomeSummaryTable(
                    ["Account", "Custody", "Currency", "Payments", "Interest received", "Interest charged", "RWT", "NRWT", "Foreign tax"], rows),
                Text("Interest from a New Zealand payer is New Zealand interest. Interest from overseas is overseas income. The platform does not record the country of an account, so this report does not say which of the two an amount is.")
            };
            if (Has(IncomeNotice.InterestCharged))
            {
                blocks.Add(Text("Interest charged to an account is listed here and is not taken off the interest received. Whether it reduces your income depends on what it was charged for."));
            }

            return new IncomeSummarySection("4. Interest", blocks);
        }

        private List<string> DividendCells(IncomeDividendSum sum) =>
        [
            Count(sum.Count), Nzd(sum.Gross), Nzd(sum.ImputationCredit), Nzd(sum.GrossWithImputation), Nzd(sum.ForeignTax),
            Nzd(sum.Rwt), Nzd(sum.Nrwt)
        ];

        private List<string> InterestCells(IncomeInterestSum sum) =>
            [Count(sum.Count), Nzd(sum.Received), Nzd(sum.Charged), Nzd(sum.Rwt), Nzd(sum.Nrwt), Nzd(sum.ForeignTax)];

        /// <summary>R13: holding rows in the order of the read's holdings, by symbol and then by PublicId.</summary>
        private IEnumerable<IncomeHoldingRow> Ordered(IEnumerable<IncomeHoldingRow> rows) =>
            rows.OrderBy(row => Instrument(row.InstrumentId).Symbol, StringComparer.Ordinal)
                .ThenBy(row => Instrument(row.InstrumentId).PublicId);

        private TaxHolding Holding(int instrumentId) => Year.Holdings.Single(holding => holding.InstrumentId == instrumentId);

        private ValuedInstrument Instrument(int instrumentId) =>
            read.Instruments.TryGetValue(instrumentId, out var instrument)
                ? instrument
                : new ValuedInstrument(instrumentId, Guid.Empty, $"#{instrumentId}", "", "");

        private string Account(int accountId) => names.AccountNames.TryGetValue(accountId, out var name) ? name : "Account";

        private int Places(string currency) => names.DecimalPlaces.TryGetValue(currency, out var places) ? places : 2;

        /// <summary>R23: an NZD figure with NZD's decimal places and no code; <c>null</c> is "not available".</summary>
        private string Nzd(decimal? amount) =>
            amount is { } value ? value.ToString("F" + Places(CustomerTaxYear.Nzd), CultureInfo.InvariantCulture) : NotAvailable;

        /// <summary>R23: a figure in another currency with its decimal places and its code.</summary>
        private string Money(decimal amount, string currency) =>
            amount.ToString("F" + Places(currency), CultureInfo.InvariantCulture) + " " + currency;

        private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

        private static IncomeSummaryText Text(string text) => new(text);
    }
}
