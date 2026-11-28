using System.Globalization;
using MyWealthV2.Application.TaxYears;
using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The content of the FIF report from one tax year read (docs/features/fif-report.md §4.3 to §4.7). No I/O. The one
/// place the words of the report are chosen. It computes no tax figure: every number is a figure of the read, or one
/// of the sums and counts R9 names.
/// </summary>
public static partial class CustomerFifReport
{
    public const string Title = "FIF Report";

    private const string NotAvailable = "not available";

    public static CustomerFifContent Build(
        CustomerTaxLedgerRead read,
        CustomerFifNames names,
        TaxResidency residency,
        DateTimeOffset generatedAt,
        DateTimeOffset ledgerReadAt)
    {
        var report = new Report(read, names, residency, generatedAt, ledgerReadAt);
        return new CustomerFifContent(
            Title, report.Header(), report.Sections(), report.Complete, names.CustomerName, read.Year.TaxYear);
    }

    /// <summary>The three groups of R18, in the order of section 3.</summary>
    private enum Group
    {
        Attributing,
        Exempt,
        NotClassified
    }

    private sealed partial class Report
    {
        private readonly CustomerTaxLedgerRead _read;
        private readonly CustomerFifNames _names;
        private readonly TaxResidency _residency;
        private readonly DateTimeOffset _generatedAt;
        private readonly DateTimeOffset _ledgerReadAt;
        private readonly List<TaxHolding> _holdings;

        public Report(
            CustomerTaxLedgerRead read,
            CustomerFifNames names,
            TaxResidency residency,
            DateTimeOffset generatedAt,
            DateTimeOffset ledgerReadAt)
        {
            _read = read;
            _names = names;
            _residency = residency;
            _generatedAt = generatedAt;
            _ledgerReadAt = ledgerReadAt;
            // R18: sorted as the read sorts them (by symbol, then by the Instrument's PublicId).
            // Sections 5 to 9 follow the order of section 3: by group, then as sorted.
            _holdings = read.Year.Holdings
                .OrderBy(GroupOf)
                .ThenBy(holding => Instrument(holding.InstrumentId).Symbol, StringComparer.Ordinal)
                .ThenBy(holding => Instrument(holding.InstrumentId).PublicId)
                .ToList();
        }

        private CustomerTaxYear Year => _read.Year;

        private int TaxYear => Year.TaxYear;

        /// <summary>R12: the tax year, the threshold test and the FIF income are each complete.</summary>
        public bool Complete => Year.Complete && Year.Threshold.Complete && Year.FifIncome.Complete;

        /// <summary>The tax holdings of the first and the third group: the ones in the FIF calculation or that may be.</summary>
        private IEnumerable<TaxHolding> Calculated => _holdings.Where(holding => GroupOf(holding) != Group.Exempt);

        public List<string> Header()
        {
            var y = TaxYear;
            var header = new List<string>
            {
                $"Tenant: {_names.TenantName}",
                $"Customer: {_names.CustomerName}",
                $"Tax year: {y} (1 April {y - 1} to 31 March {y}, New Zealand time)",
                $"Tax year in UTC: {UtcMinute(Year.From)} to {UtcMinute(Year.To)}",
                $"Generated: {Instant(_generatedAt)}",
                $"Ledger read at: {Instant(_ledgerReadAt)}",
                $"Tax residency on file: {ResidencyLabel(_residency)}",
                "All amounts are in New Zealand dollars (NZD). All times are New Zealand time."
            };
            header.Add(Complete
                ? "Status: Complete"
                : $"Status: Incomplete. {Affected().Count} of {_holdings.Count} holdings are affected. See section 9.");
            return header;
        }

        public List<CustomerFifSection> Sections()
        {
            // R25: with no tax holding, the header, section 2 and one line.
            if (_holdings.Count == 0)
            {
                return
                [
                    new CustomerFifSection("1. Summary", [new CustomerFifText("No holdings in this tax year.")]),
                    About()
                ];
            }

            return
            [
                Summary(),
                About(),
                Holdings(),
                Threshold(),
                Income(),
                .. LaterSections()
            ];
        }

        /// <summary>R15: a holding is affected when a reason of section 9 names it.</summary>
        private HashSet<int> Affected()
        {
            var affected = _holdings.Where(holding => holding.Incomplete.Count > 0).Select(holding => holding.InstrumentId).ToHashSet();
            affected.UnionWith(Year.Threshold.Incomplete.Select(reason => reason.InstrumentId).OfType<int>());
            affected.UnionWith(Year.FifIncome.Incomplete.Select(reason => reason.InstrumentId));
            affected.IntersectWith(_holdings.Select(holding => holding.InstrumentId));
            return affected;
        }

        // ----- Section 1 -----

        private CustomerFifSection Summary()
        {
            var blocks = new List<CustomerFifBlock> { Text("Threshold test: " + ThresholdSentence()) };
            blocks.AddRange(IncomeSummary().Select(line => Text(line)));
            blocks.Add(Text("These figures apply if the FIF rules apply to you. The threshold test above is one part of that question."));
            var attributing = _holdings.Count(holding => GroupOf(holding) == Group.Attributing);
            var exempt = _holdings.Count(holding => GroupOf(holding) == Group.Exempt);
            var notClassified = _holdings.Count(holding => GroupOf(holding) == Group.NotClassified);
            blocks.Add(Text(
                $"Holdings: {attributing} attributing {(attributing == 1 ? "interest" : "interests")}, {exempt} exempt or not foreign, {notClassified} not classified."));
            return new CustomerFifSection("1. Summary", blocks);
        }

        /// <summary>R17: the FIF income lines of the summary.</summary>
        private List<string> IncomeSummary()
        {
            var income = Year.FifIncome;
            if (income.Fdr is null || income.Cv is null)
                return [NotGivenSentence()];

            var lines = new List<string>
            {
                $"FIF income, fair dividend rate method: {Nzd(income.Fdr.Income)}",
                $"FIF income, comparative value method: {Nzd(income.Cv.Income)}",
                income.Lower switch
                {
                    FifLowerMethod.Fdr => "Lower: fair dividend rate",
                    FifLowerMethod.Cv => "Lower: comparative value",
                    _ => "The two totals are equal"
                }
            };
            var noFdr = Symbols(income.Incomplete
                .Where(reason => reason.Reason == FifIncomeReason.FdrNotAllowed)
                .Select(reason => reason.InstrumentId));
            if (noFdr.Count > 0)
            {
                var one = noFdr.Count == 1;
                lines.Add(
                    $"These totals do not include {string.Join(", ", noFdr)}: the fair dividend rate method is not allowed for {(one ? "it" : "them")}. {(one ? "Its" : "Their")} income is worked out separately and added to the method you use.");
            }

            return lines;
        }

        private string NotGivenSentence()
        {
            var missing = Symbols(Year.FifIncome.Incomplete
                .Where(reason => reason.Reason is FifIncomeReason.NotClassified or FifIncomeReason.HoldingIncomplete)
                .Select(reason => reason.InstrumentId));
            var count = missing.Count == 1 ? "1 holding that belongs" : $"{missing.Count} holdings that belong";
            return $"FIF income totals are not given. {count} in them could not be computed: {string.Join(", ", missing)}. A total without them would be neither a minimum nor an estimate.";
        }

        /// <summary>§4.6: the sentence of the threshold test, by its outcome.</summary>
        private string ThresholdSentence()
        {
            var test = Year.Threshold;
            var peak = Nzd(test.Peak.Total);
            var when = When(test.Peak);
            var threshold = test.Amount is { } amount ? $"The threshold for tax year {TaxYear} is {Nzd(amount)}." : null;
            if (threshold is null)
                return $"No FIF threshold is set on this platform for tax year {TaxYear}. The total cost of your attributing interests that is known reached {peak} {when}.";

            return (test.Outcome, test.Complete) switch
            {
                (ThresholdOutcome.NotAbove, _) =>
                    $"Not above the threshold. The total cost of your attributing interests was at most {peak}, {when}. {threshold}",
                (ThresholdOutcome.Above, true) =>
                    $"Above the threshold. The total cost of your attributing interests reached {peak} {when}. {threshold} Above it at any time in the year, every attributing interest is in the FIF rules for the whole year.",
                (ThresholdOutcome.Above, false) =>
                    $"Above the threshold. The costs that are known reached {peak} {when}. {threshold} Some costs are not known. They can only add to the total.",
                _ =>
                    $"The threshold test could not be completed. The costs that are known reached {peak} {when}. {threshold} Some costs are not known, so the total may have been above it."
            };
        }

        private string When(ThresholdPeak peak) =>
            peak.InstrumentId is null ? "at the start of the tax year" : $"on {Time(peak.At)}";

        // ----- Section 2 -----

        private CustomerFifSection About()
        {
            var statements = new List<CustomerFifBlock>
            {
                Text("This report is not tax advice. It does not say whether the foreign investment fund (FIF) rules apply to you, which method to use, or what tax you owe."),
                Text($"The FIF rules apply to a New Zealand tax resident who is not a transitional resident. Your tax residency on file is {ResidencyLabel(_residency)}, as recorded when this report was made. The platform does not record whether you are a transitional resident, or what your residency was during the tax year."),
                Text("This report covers the holdings recorded on this platform. Shares you hold elsewhere are in it only if your adviser has recorded them here. Shares that are not recorded count toward the threshold all the same.")
            };
            if (_holdings.Any(holding => holding.Notices.Contains(TaxNotice.ExternalFigures)))
                statements.Add(Text(CustomerTransactionsPdf.ExternalNote));
            statements.Add(Text("Both methods are shown. A person uses one method for all the holdings the totals cover in a tax year. The choice is yours."));
            statements.Add(Text("Amounts in another currency are converted to New Zealand dollars at the exchange rate of their day. Section 8 lists every price and rate used."));
            statements.Add(Text("This report shows the ledger as it was read at the time above. A report made later for the same tax year can differ, when a transaction is entered, changed in date or reversed after that time, or when a price or a rate is added."));
            return new CustomerFifSection("2. About this report", statements);
        }

        // ----- Section 3 -----

        private CustomerFifSection Holdings()
        {
            var blocks = new List<CustomerFifBlock>();
            AddGroup(blocks, Group.Attributing, "Attributing interests", null);
            AddGroup(blocks, Group.Exempt, "Exempt or not foreign",
                "These holdings are not in the FIF calculation. Their dividends and sales are taxed under the ordinary rules and are not in this report.");
            AddGroup(blocks, Group.NotClassified, "Not classified",
                "A holding that is not classified may be an attributing interest. Ask your adviser to have it classified.");
            return new CustomerFifSection("3. Holdings and classification", blocks);
        }

        private void AddGroup(List<CustomerFifBlock> blocks, Group group, string heading, string? under)
        {
            blocks.Add(new CustomerFifText(heading, Heading: true));
            var rows = _holdings.Where(holding => GroupOf(holding) == group).Select(ClassificationRow).ToList();
            blocks.Add(rows.Count == 0
                ? Text("None.")
                : new CustomerFifTable(
                    ["Symbol", "Name", "Issuer country", "Treatment", "Classified from", "Note", "Opening", "Closing", "FIF income"],
                    rows));
            if (under is not null)
                blocks.Add(Text(under));
        }

        private IReadOnlyList<string> ClassificationRow(TaxHolding holding)
        {
            var instrument = Instrument(holding.InstrumentId);
            var treatment = holding.Treatment;
            return
            [
                instrument.Symbol,
                instrument.Name,
                CountryName(treatment.IssuerCountry) ?? "not stated",
                TreatmentLabel(treatment),
                treatment.RowTaxYear is { } row ? $"classified from tax year {row}" : "",
                treatment.FifTreatment == FifTreatment.ExemptOther ? "" : treatment.Note ?? "",
                Number(holding.Opening.Quantity),
                Number(holding.Closing.Quantity),
                IncomeStatus(holding)
            ];
        }

        private static string IncomeStatus(TaxHolding holding) => holding.Income?.Status switch
        {
            FifIncomeStatus.Computed => "computed",
            FifIncomeStatus.NotComputed => "not computed",
            _ => "not in the FIF rules"
        };

        // ----- Section 4 -----

        private CustomerFifSection Threshold()
        {
            var test = Year.Threshold;
            var blocks = new List<CustomerFifBlock>
            {
                Text(test.Amount is { } amount
                    ? $"Threshold for tax year {TaxYear}: {Nzd(amount)}"
                    : $"No FIF threshold is set on this platform for tax year {TaxYear}."),
                Text($"Total cost at the start: {Nzd(test.Opening)}    Highest: {Nzd(test.Peak.Total)}, {When(test.Peak)}    At the end: {Nzd(test.Closing)}")
            };
            if (test.Amount is not null)
                blocks.Add(Text(ThresholdSentence()));
            if (test.Outcome == ThresholdOutcome.NotAbove)
                blocks.Add(Text("A person at or below the threshold does not apply the FIF rules unless they choose to. The ordinary tax rules then apply to their dividends."));

            var counted = _holdings.Where(holding => holding.Treatment.FifTreatment.IsAttributing()).ToList();
            blocks.Add(counted.Count == 0
                ? Text("No attributing interest in this tax year.")
                : new CustomerFifTable(
                    ["Symbol", "Tax cost at the start", "Tax cost at the end"],
                    counted.Select(holding => (IReadOnlyList<string>)
                    [
                        Instrument(holding.InstrumentId).Symbol,
                        CostFigure(holding.Cost, holding.Cost.Opening),
                        CostFigure(holding.Cost, holding.Cost.Closing)
                    ]).ToList()));

            if (test.Steps.Count == 0 && test.Peak.InstrumentId is null)
            {
                blocks.Add(Text("The total cost did not change during the tax year."));
            }
            else if (test.Steps.Count > 0)
            {
                blocks.Add(new CustomerFifTable(
                    ["Time", "Symbol", "Event", "Change", "Total after"],
                    test.Steps.Select(step => (IReadOnlyList<string>)
                    [
                        Time(step.Event.BookedAt),
                        Instrument(step.Event.InstrumentId).Symbol,
                        KindLabel(step.Event.Kind),
                        step.Change is { } change ? SignedNzd(change) : "not known",
                        Nzd(step.Total)
                    ]).ToList()));
            }

            if (!test.Complete)
                blocks.Add(Text("Incomplete: see section 9."));
            return new CustomerFifSection("4. Threshold test", blocks);
        }

        private string CostFigure(TaxCost cost, decimal? figure) =>
            figure is { } known ? Nzd(known) : $"not known: {UnknownCause(cost.Unknown)}";

        private string UnknownCause(TaxCostUnknown? unknown) => unknown is null
            ? "the cost could not be worked out"
            : $"{CauseLabel(unknown.Cause)} since {Time(unknown.At)}";

        // ----- Section 5 -----

        private CustomerFifSection Income()
        {
            var income = Year.FifIncome;
            var blocks = new List<CustomerFifBlock>();
            if (income.Fdr is { } fdr && income.Cv is { } cv)
            {
                blocks.Add(Text($"Fair dividend rate method (rate {Number(income.Rate)})"));
                blocks.Add(Text($"Opening value {Nzd(fdr.OpeningValue)}    {Number(income.Rate)} x opening value {Nzd(fdr.Amount)}"));
                blocks.Add(Text($"Peak holding method amounts {Nzd(fdr.PeakHoldingAmount)}    Quick sale gain amounts {Nzd(fdr.QuickSaleGainAmount)}    Quick sale adjustment {Nzd(fdr.QuickSaleAdjustment)}"));
                blocks.Add(Text($"FIF income {Nzd(fdr.Income)}"));
                blocks.Add(Text("Comparative value method"));
                blocks.Add(Text($"Total {Nzd(cv.Amount)}    FIF income {Nzd(cv.Income)}"));
                if (cv.Amount < 0m)
                    blocks.Add(Text($"The total is below zero. It gives an income of {Nzd(0m)} and cannot be used elsewhere."));
            }
            else
            {
                blocks.Add(Text(NotGivenSentence()));
            }

            blocks.Add(new CustomerFifTable(
                ["Symbol", "FIF income", "Opening value", "FDR amount", "Peak holding method amount", "Quick sale gain amount", "CV amount"],
                Calculated.Select(IncomeRow).ToList()));
            blocks.Add(Text("The quick sale adjustment is the lesser of the two totals over all holdings, not a sum per holding."));
            return new CustomerFifSection("5. FIF income", blocks);
        }

        private IReadOnlyList<string> IncomeRow(TaxHolding holding)
        {
            var symbol = Instrument(holding.InstrumentId).Symbol;
            if (holding.Income is not { Status: FifIncomeStatus.Computed } income)
                return [symbol, IncomeStatus(holding), "", "", "", "", ""];

            return
            [
                symbol,
                "computed",
                Nzd(income.Fdr?.OpeningValue),
                Nzd(income.Fdr?.Amount),
                Nzd(income.QuickSale?.PeakHoldingAmount),
                Nzd(income.QuickSale?.GainAmount),
                Nzd(income.Cv?.Amount)
            ];
        }

        // ----- Helpers -----

        private ValuedInstrument Instrument(int instrumentId) =>
            _read.Instruments.TryGetValue(instrumentId, out var instrument)
                ? instrument
                : new ValuedInstrument(instrumentId, Guid.Empty, $"#{instrumentId}", "", "");

        private List<string> Symbols(IEnumerable<int> instrumentIds)
        {
            var ids = instrumentIds.ToHashSet();
            return _holdings.Where(holding => ids.Contains(holding.InstrumentId))
                .Select(holding => Instrument(holding.InstrumentId).Symbol)
                .ToList();
        }

        private static Group GroupOf(TaxHolding holding) => holding.Treatment.FifTreatment switch
        {
            FifTreatment.Attributing or FifTreatment.AttributingNoFdr => Group.Attributing,
            FifTreatment.NotClassified => Group.NotClassified,
            _ => Group.Exempt
        };

        private int Places(string currency) => _names.DecimalPlaces.TryGetValue(currency, out var places) ? places : 2;

        /// <summary>R14: an NZD figure with NZD's decimal places and no code; <c>null</c> is "not available".</summary>
        private string Nzd(decimal? amount) =>
            amount is { } value ? value.ToString("F" + Places(CustomerTaxYear.Nzd), CultureInfo.InvariantCulture) : NotAvailable;

        private string SignedNzd(decimal amount) => amount > 0m ? "+" + Nzd(amount) : Nzd(amount);

        /// <summary>R14: a figure in another currency with its decimal places and its code.</summary>
        private string Money(decimal? amount, string currency) =>
            amount is { } value
                ? value.ToString("F" + Places(currency), CultureInfo.InvariantCulture) + " " + currency
                : NotAvailable;

        private static CustomerFifText Text(string text) => new(text);
    }

    /// <summary>R14: a quantity or a rate as the read gives it, with up to 8 decimal places and no trailing zeros.</summary>
    internal static string Number(decimal value) =>
        Math.Round(value, 8, MidpointRounding.AwayFromZero).ToString("0.########", CultureInfo.InvariantCulture);

    internal static string Signed(decimal value) => value > 0m ? "+" + Number(value) : Number(value);

    /// <summary>R13: an instant in New Zealand time, or in UTC with "UTC" after it before the rule of 2007.</summary>
    internal static string Time(DateTimeOffset instant) =>
        NewZealandTime.Local(instant) is { } local
            ? local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : UtcMinute(instant) + " UTC";

    /// <summary>R15: "{New Zealand time} New Zealand time ({UTC} UTC)".</summary>
    private static string Instant(DateTimeOffset instant) =>
        $"{Time(instant)} New Zealand time ({instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC)";

    private static string UtcMinute(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    internal static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ResidencyLabel(TaxResidency residency) =>
        residency == TaxResidency.NewZealand ? "New Zealand" : "Overseas";

    /// <summary>R18: the issuer country by its name in the list of instrument-tax-attributes R6.</summary>
    private static string? CountryName(string? code) =>
        code is null ? null : Domain.Instruments.Countries.All.FirstOrDefault(country => country.Code == code)?.Name ?? code;

    /// <summary>§4.6: the label of a treatment.</summary>
    private static string TreatmentLabel(TaxTreatment treatment) => treatment.FifTreatment switch
    {
        FifTreatment.Attributing => "Attributing interest",
        FifTreatment.AttributingNoFdr => "Attributing interest, fair dividend rate not allowed",
        FifTreatment.ExemptAsxListed => "Exempt: ASX-listed Australian share",
        FifTreatment.ExemptOther => $"Exempt: {treatment.Note}",
        FifTreatment.NotForeign => "Not foreign (New Zealand)",
        _ => "Not classified"
    };

    /// <summary>§4.6: the label of a tax event.</summary>
    internal static string KindLabel(TaxEventKind kind) => kind switch
    {
        TaxEventKind.Acquisition => "Purchase",
        TaxEventKind.Disposal => "Sale",
        TaxEventKind.Dividend => "Dividend",
        TaxEventKind.Reorganisation => "Share reorganisation",
        TaxEventKind.Arrival => "Transferred in, at market value",
        TaxEventKind.Departure => "Transferred out, at market value",
        TaxEventKind.Move => "Moved between your accounts",
        _ => "Correction"
    };

    /// <summary>§4.7: the cause of an unknown tax cost (tax-cost-threshold R13).</summary>
    private static string CauseLabel(TaxCostUnknownCause cause) => cause switch
    {
        TaxCostUnknownCause.NoCarriedCost => "it was transferred in with no cost stated",
        TaxCostUnknownCause.NoRate => "no exchange rate is stored for the day of a purchase",
        TaxCostUnknownCause.StaleRate => "the exchange rate stored for the day of a purchase is too old",
        TaxCostUnknownCause.NegativeQuantity => "more was sold or transferred out than was held",
        _ => "a correction left a cost below 0"
    };

    /// <summary>§4.6: the label of a status that leaves a figure missing.</summary>
    internal static string StatusLabel(ValuationLineStatus status) => status switch
    {
        ValuationLineStatus.NoRate => "No exchange rate",
        ValuationLineStatus.StaleRate => "Exchange rate too old",
        ValuationLineStatus.NoPrice => "No price",
        ValuationLineStatus.StalePrice => "Price too old",
        ValuationLineStatus.PriceBeforeCorporateAction => "Price from before a corporate action",
        ValuationLineStatus.NegativeQuantity => "Quantity below 0",
        _ => ""
    };

    /// <summary>§4.6: the label of the status of an event's amount, when it is missing.</summary>
    internal static string StatusLabel(TaxAmountStatus status) => status switch
    {
        TaxAmountStatus.NoRate => "No exchange rate",
        TaxAmountStatus.StaleRate => "Exchange rate too old",
        TaxAmountStatus.NoPrice => "No price",
        TaxAmountStatus.StalePrice => "Price too old",
        _ => ""
    };
}
