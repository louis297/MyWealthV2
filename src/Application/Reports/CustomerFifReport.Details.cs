using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Application.Reports;

public static partial class CustomerFifReport
{
    private sealed partial class Report
    {
        /// <summary>Sections 6 to 9 (R21 to R24).</summary>
        private IEnumerable<CustomerFifSection> LaterSections() =>
        [
            new("6. Each holding", Calculated.SelectMany(HoldingBlock).ToList()),
            TaxTaken(),
            Used(),
            WhatToKnow()
        ];

        // ----- Section 6 -----

        private IEnumerable<CustomerFifBlock> HoldingBlock(TaxHolding holding)
        {
            var instrument = Instrument(holding.InstrumentId);
            var treatment = holding.Treatment;
            yield return new CustomerFifText($"{instrument.Symbol} {instrument.Name}", Heading: true);
            var classified = treatment.RowTaxYear is { } row ? $"  classified from tax year {row}" : "";
            yield return Text($"{CountryName(treatment.IssuerCountry) ?? "Issuer country not stated"}  {TreatmentLabel(treatment)}{classified}");

            foreach (var block in Point("Opening", holding.Opening))
                yield return block;
            foreach (var block in Point("Closing", holding.Closing))
                yield return block;

            yield return Text("Events");
            if (holding.Events.Count == 0)
            {
                yield return Text("No events in this tax year.");
            }
            else
            {
                yield return new CustomerFifTable(
                    ["#", "Time", "Event", "Account", "Quantity", "Quantity after", "Comparable after", "Amount", "Rate", "Cost change", "Tax cost after"],
                    holding.Events.Select(EventRow).ToList());
            }

            var dividends = holding.Events.Where(e => e.Dividend is not null).ToList();
            if (dividends.Count > 0)
            {
                yield return Text("Dividends");
                yield return new CustomerFifTable(
                    ["#", "Shares", "Gross", "Foreign tax", "RWT", "NRWT", "Imputation credit", "Tax source"],
                    dividends.Select(e => (IReadOnlyList<string>)
                    [
                        e.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Number(e.Dividend!.Shares),
                        Nzd(e.Dividend.Gross),
                        Nzd(e.Dividend.ForeignTax),
                        Nzd(e.Dividend.Rwt),
                        Nzd(e.Dividend.Nrwt),
                        Nzd(e.Dividend.ImputationCredit),
                        e.Dividend.TaxSource == DividendTaxSource.Foreign ? "Foreign" : "New Zealand"
                    ]).ToList());
            }

            yield return Text(holding.Cost.Status switch
            {
                TaxCostStatus.Known => $"Tax cost: {Nzd(holding.Cost.Opening)} at the start, {Nzd(holding.Cost.Closing)} at the end",
                TaxCostStatus.Unknown => $"Tax cost: not known: {UnknownCause(holding.Cost.Unknown)}",
                _ => "Tax cost: not needed"
            });

            foreach (var line in IncomeLines(holding))
                yield return Text(line);

            var missing = ReasonsOf(holding).Select(ReasonShortName).Distinct().ToList();
            if (missing.Count > 0)
                yield return Text($"Missing: {string.Join("; ", missing)}. See section 9.");
            if (holding.Notices.Count > 0)
                yield return Text($"Notices: {string.Join("; ", holding.Notices.Select(NoticeShortName))}. See section 9.");
        }

        private IEnumerable<CustomerFifBlock> Point(string name, TaxPoint point)
        {
            yield return Text($"{name}, valued as of {Date(point.ValuationDay)}");
            if (point.Quantity == 0m)
            {
                yield return Text("Nothing held.");
                yield break;
            }

            yield return new CustomerFifTable(
                ["Account", "Custody", "Quantity", "Price", "Market value", "Rate", "Value"],
                point.Lines.Select(LineRow).ToList());
            var comparable = point.ComparableQuantity is { } restated && restated != point.Quantity
                ? $" ({Number(restated)} on the basis of the year's end)"
                : "";
            yield return Text($"Total {Number(point.Quantity)}{comparable}  {Nzd(point.Value)}");
        }

        private IReadOnlyList<string> LineRow(TaxValueLine valueLine)
        {
            var line = valueLine.Line;
            var currency = line.Price?.Currency ?? Instrument(line.InstrumentId).QuoteCurrency;
            var rate = line.Fx is { } fx
                ? RateText(fx)
                : line.Status is ValuationLineStatus.NoRate or ValuationLineStatus.StaleRate
                    ? StatusLabel(line.Status)
                    : line.Value is null ? NotAvailable : "no conversion";
            return
            [
                Account(valueLine.AccountId),
                CustodyLabel(valueLine.CustodyMode),
                Number(line.Quantity),
                line.Price is { } price ? $"{Money(price.Amount, price.Currency)} ({Date(price.AsOfDate)})" : StatusLabel(line.Status),
                Money(line.MarketValue, currency),
                rate,
                Nzd(line.Value)
            ];
        }

        private IReadOnlyList<string> EventRow(TaxYearEvent e)
        {
            var cost = e.Cost;
            var costChange = cost switch
            {
                null => "",
                { Change: null, After: null } => "not known",
                { Change: { } change } when change != 0m => SignedNzd(change),
                _ => ""
            };
            return
            [
                e.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Time(e.Event.BookedAt),
                KindLabel(e.Event.Kind),
                Account(e.Event.AccountId),
                Signed(e.Event.Quantity),
                Number(e.QuantityAfter),
                e.ComparableQuantityAfter is { } comparable ? Number(comparable) : NotAvailable,
                AmountText(e),
                e.Fx is { } fx ? RateText(fx) : "",
                costChange,
                cost is null ? "" : cost.After is { } after ? Nzd(after) : "not known"
            ];
        }

        /// <summary>R21 (c): the amount of an event, with what it is made of.</summary>
        private string AmountText(TaxYearEvent e)
        {
            if (e.Event.Move is { } move)
                return $"{Number(move.Quantity)} from {Account(move.From.AccountId)} ({Time(move.From.BookedAt)}) to {Account(move.To.AccountId)}";

            if (e.Amount is not { } amount)
            {
                var label = StatusLabel(e.Status);
                return label.Length == 0 ? "" : $"{NotAvailable} ({label})";
            }

            var text = Nzd(amount);
            switch (e.Event.Kind)
            {
                case TaxEventKind.Acquisition when e.Fee is > 0m && e.Principal is { } principal:
                    return $"{text} ({Nzd(principal)} and a fee of {Nzd(e.Fee)})";
                case TaxEventKind.Disposal when e.Fee is > 0m && e.Principal is { } principal:
                    return $"{text} ({Nzd(principal)} less a fee of {Nzd(e.Fee)})";
                case TaxEventKind.Arrival or TaxEventKind.Departure:
                    if (e.Price is { } price)
                        text += $" at {Money(price.Amount, price.Currency)} ({Date(price.AsOfDate)})";
                    if (e.Event is { Kind: TaxEventKind.Arrival, CarriedCost: { } carried })
                    {
                        text += $"; carried cost {Money(carried.Amount, carried.Currency)}";
                        if (e.Cost?.Fx is { } fx)
                            text += " " + RateText(fx);
                        if (carried.AcquiredOn is { } acquiredOn)
                            text += $", acquired on {Date(acquiredOn)}";
                    }

                    return text;
                default:
                    return text;
            }
        }

        /// <summary>R21 (f): the FIF income of the holding, or the sentence of why it is not computed.</summary>
        private IEnumerable<string> IncomeLines(TaxHolding holding)
        {
            var income = holding.Income;
            if (income is not { Status: FifIncomeStatus.Computed })
            {
                yield return income?.Reason switch
                {
                    FifIncomeReason.NotClassified => NotClassifiedSentence,
                    FifIncomeReason.FdrNotAllowed => FdrNotAllowedSentence,
                    _ => "Its FIF income is not computed: a figure it needs is missing. See section 9."
                };
                yield break;
            }

            if (income.Fdr is { } fdr)
                yield return $"Fair dividend rate: {Number(Year.FifIncome.Rate)} x {Nzd(fdr.OpeningValue)} = {Nzd(fdr.Amount)}";

            var quickSale = income.QuickSale;
            if (quickSale is { PurchasedQuantity: > 0m, Sales.Count: > 0 })
            {
                yield return $"Quick sale: opening {Number(quickSale.OpeningQuantity)}, peak {Number(quickSale.PeakQuantity)}, closing {Number(quickSale.ClosingQuantity)}; peak holding differential {Number(quickSale.PeakHoldingDifferential)}";
                var average = quickSale.AverageCost is { } cost ? Number(cost) : NotAvailable;
                yield return $"Purchases of the year: {Number(quickSale.PurchasedQuantity)} shares for {Nzd(quickSale.PurchaseCost)}, average cost {average}";
                yield return $"Peak holding method amount {Nzd(quickSale.PeakHoldingAmount)}    Shares matched {Number(quickSale.MatchedQuantity)}    Quick sale gain amount {Nzd(quickSale.GainAmount)}";
                foreach (var match in quickSale.Matches)
                    yield return $"{Number(match.Quantity)} of purchase {match.PurchaseSequence} sold in sale {match.SaleSequence}";
                foreach (var sale in quickSale.Sales.Where(sale => sale.MatchedQuantity > 0m))
                    yield return $"Sale {sale.Sequence}: {Number(sale.MatchedQuantity)} of {Number(sale.Quantity)} shares matched, {Nzd(sale.Proceeds)} of its proceeds of {Nzd(sale.Amount)}";
                foreach (var share in quickSale.DividendShares.Where(share => share.MatchedQuantity > 0m))
                    yield return $"Dividend {share.Sequence}: {Nzd(share.Share)} of {Nzd(share.Amount)}, for {Number(share.MatchedQuantity)} of {Number(share.HeldQuantity)} shares held";
                if (quickSale.MatchedQuantity > 0m)
                    yield return $"Proceeds of the shares matched {Nzd(quickSale.Proceeds)}    Their dividends {Nzd(quickSale.Dividends)}    At the average cost {Nzd(quickSale.Cost)}";
            }
            else
            {
                yield return "No quick sale in this tax year.";
            }

            if (income.Cv is { } cv)
                yield return $"Comparative value: ({Nzd(cv.ClosingValue)} + {Nzd(cv.Sales)} + {Nzd(cv.Dividends)}) - ({Nzd(cv.OpeningValue)} + {Nzd(cv.Purchases)}) = {Nzd(cv.Amount)}";
        }

        // ----- Section 7 -----

        private CustomerFifSection TaxTaken()
        {
            const string title = "7. Tax taken from dividends";
            var rows = Calculated
                .Select(holding => (Holding: holding, Dividends: holding.Events.Select(e => e.Dividend).OfType<TaxDividendAmounts>().ToList()))
                .Where(row => row.Dividends.Count > 0)
                .Select(row => (row.Holding, Country: CountryName(row.Holding.Treatment.IssuerCountry), Sums: Sums(row.Dividends)))
                .ToList();
            if (rows.Count == 0)
                return new CustomerFifSection(title, [Text("No dividends on these holdings in this tax year.")]);

            var perHolding = rows.Select(row =>
            {
                var computed = row.Holding.Income is { Status: FifIncomeStatus.Computed } income ? income : null;
                return (IReadOnlyList<string>)
                [
                    Instrument(row.Holding.InstrumentId).Symbol,
                    row.Country ?? "not stated",
                    .. row.Sums.Select(Nzd),
                    computed is null ? "not computed" : Nzd(computed.Fdr?.Amount),
                    computed is null ? "not computed" : Nzd(computed.Cv?.Amount)
                ];
            }).ToList();

            var perCountry = rows
                .GroupBy(row => row.Country)
                .OrderBy(group => group.Key is null)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (IReadOnlyList<string>)
                [
                    group.Key ?? "Country not stated",
                    .. Add(group.Select(row => row.Sums)).Select(Nzd)
                ])
                .Append(["Total", .. Add(rows.Select(row => row.Sums)).Select(Nzd)])
                .ToList();

            return new CustomerFifSection(title,
            [
                new CustomerFifTable(
                    ["Symbol", "Issuer country", "Dividends", "Foreign tax", "RWT", "NRWT", "Imputation credits", "FDR amount", "CV amount"],
                    perHolding),
                new CustomerFifTable(["Country", "Dividends", "Foreign tax", "RWT", "NRWT", "Imputation credits"], perCountry),
                Text("Foreign tax taken from a dividend may give a foreign tax credit. The credit is limited, and this report does not work it out. Resident withholding tax (RWT) is offset against the tax you owe. See Inland Revenue's guide to foreign investment funds (IR461).")
            ]);
        }

        /// <summary>R22: the gross, the foreign tax, the RWT, the NRWT and the imputation credit, each summed; a sum with a missing figure is missing.</summary>
        private static decimal?[] Sums(IReadOnlyList<TaxDividendAmounts> dividends) =>
        [
            Sum(dividends.Select(d => d.Gross)),
            Sum(dividends.Select(d => d.ForeignTax)),
            Sum(dividends.Select(d => d.Rwt)),
            Sum(dividends.Select(d => d.Nrwt)),
            Sum(dividends.Select(d => d.ImputationCredit))
        ];

        private static decimal?[] Add(IEnumerable<decimal?[]> sums)
        {
            var list = sums.ToList();
            return Enumerable.Range(0, 5).Select(column => Sum(list.Select(row => row[column]))).ToArray();
        }

        private static decimal? Sum(IEnumerable<decimal?> figures)
        {
            var total = 0m;
            foreach (var figure in figures)
            {
                if (figure is not { } value)
                    return null;
                total += value;
            }

            return total;
        }

        // ----- Section 8 -----

        private CustomerFifSection Used()
        {
            var prices = new List<(int Order, string Symbol, StoredPrice Price)>();
            var rates = new List<(string Currency, StoredRate Rate)>();
            var order = 0;
            foreach (var holding in Calculated)
            {
                var instrument = Instrument(holding.InstrumentId);
                foreach (var line in holding.Opening.Lines.Concat(holding.Closing.Lines).Select(valueLine => valueLine.Line))
                {
                    if (line.Price is { } price)
                        prices.Add((order, instrument.Symbol, price));
                    if (line.Fx is { } fx)
                        rates.Add((line.Price?.Currency ?? instrument.QuoteCurrency, fx));
                }

                foreach (var e in holding.Events)
                {
                    if (e.Event.Kind is TaxEventKind.Arrival or TaxEventKind.Departure && e.Price is { } price)
                        prices.Add((order, instrument.Symbol, price));
                    if (e.Fx is { } fx)
                        rates.Add((e.Event.Trade?.Currency ?? e.Event.Dividend?.Currency ?? e.Price?.Currency ?? instrument.QuoteCurrency, fx));
                    if (e.Cost?.Fx is { } costFx)
                        rates.Add((e.Event.CarriedCost?.Currency ?? e.Event.Trade?.Currency ?? instrument.QuoteCurrency, costFx));
                }

                order++;
            }

            var blocks = new List<CustomerFifBlock> { new CustomerFifText("Prices", Heading: true) };
            var priceRows = prices
                .DistinctBy(row => (row.Symbol, row.Price))
                .OrderBy(row => row.Order).ThenBy(row => row.Price.AsOfDate)
                .Select(row => (IReadOnlyList<string>)
                    [row.Symbol, Date(row.Price.AsOfDate), Money(row.Price.Amount, row.Price.Currency), row.Price.Source])
                .ToList();
            blocks.Add(priceRows.Count == 0 ? Text("None.") : new CustomerFifTable(["Symbol", "Date", "Price", "Source"], priceRows));

            blocks.Add(new CustomerFifText("Rates", Heading: true));
            var rateRows = rates
                .Distinct()
                .OrderBy(row => row.Currency, StringComparer.Ordinal).ThenBy(row => row.Rate.AsOfDate)
                .Select(row => (IReadOnlyList<string>)
                    [$"{row.Currency} to NZD", Date(row.Rate.AsOfDate), Number(row.Rate.Rate), row.Rate.Source])
                .ToList();
            blocks.Add(rateRows.Count == 0 ? Text("None.") : new CustomerFifTable(["Pair", "Date", "Rate", "Source"], rateRows));

            var threshold = Year.Threshold.Amount is { } amount ? Nzd(amount) : "not set";
            blocks.Add(Text($"FIF threshold of tax year {TaxYear}: {threshold}    Fair dividend rate of tax year {TaxYear}: {Number(Year.FifIncome.Rate)}"));
            blocks.Add(Text($"Ledger read at {Time(_ledgerReadAt)}"));
            blocks.Add(Text("The tax cost at the start of the tax year comes from purchases of earlier tax years. Their rates are not listed here."));
            return new CustomerFifSection("8. Prices, rates and values used", blocks);
        }

        // ----- Section 9 -----

        private CustomerFifSection WhatToKnow()
        {
            var blocks = new List<CustomerFifBlock>();
            var missing = new List<string>();
            if (Year.Threshold.Incomplete.Any(reason => reason.Reason == ThresholdIncompleteReason.NoThreshold))
                missing.Add($"No FIF threshold is set on this platform for tax year {TaxYear}.");
            foreach (var holding in _holdings)
            {
                var symbol = Instrument(holding.InstrumentId).Symbol;
                missing.AddRange(MissingSentences(holding).Select(sentence => $"{symbol}: {sentence}"));
            }

            if (missing.Count > 0)
            {
                blocks.Add(new CustomerFifText("What is missing", Heading: true));
                blocks.AddRange(missing.Select(Text));
            }

            var notices = Calculated
                .SelectMany(holding => holding.Notices.Select(notice => $"{Instrument(holding.InstrumentId).Symbol}: {NoticeSentence(notice)}"))
                .ToList();
            if (notices.Count > 0)
            {
                blocks.Add(new CustomerFifText("What these figures rest on", Heading: true));
                blocks.AddRange(notices.Select(Text));
            }

            if (blocks.Count == 0)
                blocks.Add(Text("Nothing to report."));
            return new CustomerFifSection("9. What to know about these figures", blocks);
        }

        /// <summary>
        /// R24: the reasons that name a holding, from the tax holding, the threshold test and the FIF income, each once:
        /// <c>NotClassified</c> is one reason, and <c>HoldingIncomplete</c> stands for the holding's own reasons.
        /// </summary>
        private IEnumerable<string> ReasonsOf(TaxHolding holding)
        {
            foreach (var reason in holding.Incomplete)
                yield return reason.ToString();
            foreach (var reason in Year.Threshold.Incomplete.Where(reason => reason.InstrumentId == holding.InstrumentId))
                yield return reason.Reason.ToString();
            foreach (var reason in Year.FifIncome.Incomplete.Where(reason =>
                         reason.InstrumentId == holding.InstrumentId && reason.Reason != FifIncomeReason.HoldingIncomplete))
                yield return reason.Reason.ToString();
        }

        private IEnumerable<string> MissingSentences(TaxHolding holding)
        {
            foreach (var reason in ReasonsOf(holding).Distinct())
            {
                switch (reason)
                {
                    case nameof(TaxIncompleteReason.NotClassified):
                        yield return NotClassifiedSentence;
                        break;
                    case nameof(TaxIncompleteReason.OpeningNotValued):
                        yield return $"No market value at the start of the tax year ({NotValuedLabel(holding.Opening)}). Its FIF income is not computed.";
                        break;
                    case nameof(TaxIncompleteReason.ClosingNotValued):
                        yield return $"No market value at the end of the tax year ({NotValuedLabel(holding.Closing)}). Its FIF income is not computed.";
                        break;
                    case nameof(TaxIncompleteReason.AmountMissing):
                        foreach (var e in holding.Events.Where(e => e.Amount is null && StatusLabel(e.Status).Length > 0))
                            yield return $"An amount of event {e.Sequence} could not be worked out ({StatusLabel(e.Status)}). Its FIF income is not computed.";
                        break;
                    case nameof(TaxIncompleteReason.NegativeQuantity):
                        yield return "The quantity is below 0 at some point of the tax year. An entry is missing or was dated too late.";
                        break;
                    case nameof(TaxIncompleteReason.NoComparableQuantity):
                        yield return "A share reorganisation was booked on an account that held nothing, so quantities before and after it cannot be compared.";
                        break;
                    case nameof(ThresholdIncompleteReason.CostUnknown):
                        yield return $"The cost is not known: {UnknownCause(holding.Cost.Unknown)}. It is not in the total of the threshold test.";
                        break;
                    case nameof(ThresholdIncompleteReason.TreatmentChanged):
                        yield return "Its classification changed while you held it. Tax law then starts the cost again from a market value, which this platform does not work out. The cost shown is the cost paid.";
                        break;
                    case nameof(FifIncomeReason.FdrNotAllowed):
                        yield return FdrNotAllowedSentence;
                        break;
                }
            }
        }

        private static string NotValuedLabel(TaxPoint point) =>
            point.Lines.Select(line => line.Line.Status).FirstOrDefault(status => status != ValuationLineStatus.Valued) is var status
            && status != ValuationLineStatus.Valued
                ? StatusLabel(status)
                : "No price";

        private string NotClassifiedSentence =>
            $"Not classified for tax year {TaxYear}. It may be an attributing interest. It is in no total, the threshold test cannot be completed, and FIF income totals are not given.";

        private string Account(int accountId) =>
            _names.AccountNames.TryGetValue(accountId, out var name) ? name : "Account";
    }

    private const string FdrNotAllowedSentence =
        "The fair dividend rate method is not allowed for this holding. Its income is not computed here and is in no total. It is worked out under the comparative value method on its own, or the deemed rate of return.";

    private static string RateText(StoredRate rate) => $"x {Number(rate.Rate)} ({Date(rate.AsOfDate)})";

    /// <summary>Custody-mode-grouping R14.</summary>
    private static string CustodyLabel(CustodyMode mode) =>
        mode == CustodyMode.Custodial ? "Held on platform" : "Held elsewhere";

    /// <summary>R21 (g): the short name of a reason.</summary>
    private static string ReasonShortName(string reason) => reason switch
    {
        nameof(TaxIncompleteReason.NotClassified) => "Not classified",
        nameof(TaxIncompleteReason.OpeningNotValued) => "No market value at the start",
        nameof(TaxIncompleteReason.ClosingNotValued) => "No market value at the end",
        nameof(TaxIncompleteReason.AmountMissing) => "An amount missing",
        nameof(TaxIncompleteReason.NegativeQuantity) => "Quantity below 0",
        nameof(TaxIncompleteReason.NoComparableQuantity) => "Quantities not comparable",
        nameof(ThresholdIncompleteReason.CostUnknown) => "Cost not known",
        nameof(ThresholdIncompleteReason.TreatmentChanged) => "Classification changed",
        nameof(FifIncomeReason.FdrNotAllowed) => "Fair dividend rate not allowed",
        _ => reason
    };

    /// <summary>R21 (g): the short name of a notice.</summary>
    private static string NoticeShortName(TaxNotice notice) => notice switch
    {
        TaxNotice.ExternalFigures => "Held elsewhere",
        TaxNotice.ArrivalAtMarketValue => "Transferred in at market value",
        TaxNotice.DepartureAtMarketValue => "Transferred out at market value",
        TaxNotice.Move => "Moved between your accounts",
        TaxNotice.Scrip => "Shares in place of a dividend",
        TaxNotice.Correction => "Correction",
        TaxNotice.DividendTaxSourceDisagrees => "Tax source differs from the issuer country",
        _ => "Cost carried in"
    };

    /// <summary>§4.7: what a notice says.</summary>
    private static string NoticeSentence(TaxNotice notice) => notice switch
    {
        TaxNotice.ExternalFigures => "Some of these figures come from an account held elsewhere. They were recorded by your adviser from outside statements, and the platform has not checked them.",
        TaxNotice.ArrivalAtMarketValue => "Shares were transferred in. They are counted as bought at the market value of that day, because the platform does not know when they were first acquired.",
        TaxNotice.DepartureAtMarketValue => "Shares were transferred out. They are counted as sold at the market value of that day, because the platform does not know what became of them.",
        TaxNotice.Move => "A transfer out of one of your accounts and a transfer into another were read as one move, not as a sale and a purchase. Nothing in the ledger links the two entries.",
        TaxNotice.Scrip => "Shares were issued in place of a dividend. They are read as a share reorganisation. The platform cannot know whether the issue was a taxable dividend.",
        TaxNotice.Correction => "A correction changed this holding. It is not counted as a purchase or a sale.",
        TaxNotice.DividendTaxSourceDisagrees => "A dividend states a tax source that does not agree with the issuer country of the holding. Both are shown as recorded.",
        _ => "The cost of shares that were transferred in is the cost stated at the transfer, converted at one exchange rate."
    };
}
