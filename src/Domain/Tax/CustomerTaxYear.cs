using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.Tax;

/// <summary>
/// The tax year of one Customer (docs/features/tax-year-events.md): per Instrument, the opening and the closing and
/// the tax events between them in NZD. A value worked out by <see cref="Compute"/>, which reads no clock and no store;
/// nothing is stored.
/// </summary>
public sealed record CustomerTaxYear(
    int TaxYear,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<TaxHolding> Holdings,
    bool Complete,
    int IncompleteCount)
{
    /// <summary>
    /// R18, R21–R29 over the events of <see cref="TaxLedger.Build"/>. <paramref name="treatments"/> holds the treatment
    /// of each Instrument for the year (none: not classified); <paramref name="rates"/> the rate row into NZD read for a
    /// currency and the UTC date of a <c>bookedAt</c> (none: no row of the pair); <paramref name="values"/> the lines
    /// of the opening, the closing and each arrival or departure of the year; <paramref name="decimalPlaces"/> NZD's.
    /// </summary>
    public static CustomerTaxYear Compute(
        int taxYear,
        IReadOnlyList<TaxEvent> events,
        IReadOnlyDictionary<int, TaxTreatment> treatments,
        IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates,
        TaxYearValues values,
        int decimalPlaces)
    {
        var from = Tax.TaxYear.Start(taxYear);
        var to = Tax.TaxYear.End(taxYear);
        var ofYear = events.Where(e => e.BookedAt >= from && e.BookedAt < to).ToList();
        var opening = TaxLedger.QuantitiesAt(events, from);
        var closing = TaxLedger.QuantitiesAt(events, to);

        // R23: a tax holding is listed when it is held at the start or has an event in the year.
        var instruments = opening.Select(quantity => quantity.InstrumentId)
            .Concat(ofYear.Select(e => e.InstrumentId))
            .Distinct()
            .Order();
        var holdings = new List<TaxHolding>();
        foreach (var instrumentId in instruments)
        {
            var treatment = treatments.GetValueOrDefault(instrumentId) ?? TaxTreatment.NotClassified();
            var needsValue = NeedsValue(treatment.FifTreatment);
            var start = Point(
                from, Tax.TaxYear.OpeningDay(taxYear), Of(opening, instrumentId), Lines(values.Opening, instrumentId), needsValue);
            var end = Point(
                to, Tax.TaxYear.ClosingDay(taxYear), Of(closing, instrumentId), Lines(values.Closing, instrumentId), needsValue);

            var holdingEvents = ofYear.Where(e => e.InstrumentId == instrumentId).ToList();
            var comparable = Comparable(Of(opening, instrumentId), holdingEvents);
            var quantity = start.Quantity;
            var yearEvents = new List<TaxYearEvent>();
            foreach (var taxEvent in holdingEvents)
            {
                quantity += taxEvent.Changes.Sum(change => change.Quantity);
                yearEvents.Add(Amounts(yearEvents.Count + 1, taxEvent, rates, values, decimalPlaces) with
                {
                    QuantityAfter = quantity,
                    ComparableQuantityAfter = comparable?[yearEvents.Count + 1]
                });
            }

            // R24: at the closing the comparable quantity is the quantity; R25: the peak of the opening and every event.
            start = start with { ComparableQuantity = comparable?[0] };
            end = end with { ComparableQuantity = comparable is null ? null : end.Quantity };
            holdings.Add(new TaxHolding(
                instrumentId,
                treatment,
                start,
                end,
                comparable?.Max(),
                yearEvents,
                Reasons(treatment, start, end, yearEvents, comparable is not null),
                Notices(treatment, Of(opening, instrumentId), yearEvents)));
        }

        var incomplete = holdings.Count(holding => !holding.Complete);
        return new CustomerTaxYear(taxYear, from, to, holdings, incomplete == 0, incomplete);
    }

    /// <summary>
    /// R24: the comparable quantity of the opening (index 0) and after each event (index <c>k + 1</c>), or none when a
    /// reorganisation leg on an Account that holds nothing before it leaves no factor. A reorganisation leg's factor is
    /// its Account's quantity after the leg over the quantity before it; each Account's quantity at a point is multiplied
    /// by the factors of its legs after that point, multiplied first and divided after, and cut to 8 decimal places.
    /// </summary>
    private static decimal[]? Comparable(IReadOnlyList<TaxQuantity> opening, IReadOnlyList<TaxEvent> events)
    {
        var states = new List<Dictionary<int, decimal>> { opening.ToDictionary(q => q.AccountId, q => q.Quantity) };
        foreach (var taxEvent in events)
        {
            var next = new Dictionary<int, decimal>(states[^1]);
            foreach (var change in taxEvent.Changes)
            {
                next[change.AccountId] = next.GetValueOrDefault(change.AccountId) + change.Quantity;
            }

            states.Add(next);
        }

        var factors = new List<(int Index, int AccountId, decimal After, decimal Before)>();
        for (var k = 0; k < events.Count; k++)
        {
            if (events[k].Kind != TaxEventKind.Reorganisation)
            {
                continue;
            }

            var accountId = events[k].AccountId;
            var before = states[k].GetValueOrDefault(accountId);
            if (before == 0m)
            {
                return null;
            }

            factors.Add((k, accountId, states[k + 1].GetValueOrDefault(accountId), before));
        }

        var comparable = new decimal[states.Count];
        for (var point = 0; point < states.Count; point++)
        {
            var sum = 0m;
            foreach (var (accountId, quantity) in states[point])
            {
                var later = factors.Where(f => f.AccountId == accountId && f.Index >= point).ToList();
                var multiplied = later.Aggregate(quantity, (product, f) => product * f.After);
                var divisor = later.Aggregate(1m, (product, f) => product * f.Before);
                sum += decimal.Round(multiplied / divisor, 8, MidpointRounding.ToZero);
            }

            comparable[point] = sum;
        }

        return comparable;
    }

    /// <summary>R27: every reason of the table that applies, in the order of the table.</summary>
    private static List<TaxIncompleteReason> Reasons(
        TaxTreatment treatment, TaxPoint opening, TaxPoint closing, IReadOnlyList<TaxYearEvent> events, bool hasComparable)
    {
        var reasons = new List<TaxIncompleteReason>();
        if (treatment.FifTreatment == FifTreatment.NotClassified)
        {
            reasons.Add(TaxIncompleteReason.NotClassified);
        }

        if (opening.Status == TaxValueStatus.NotValued)
        {
            reasons.Add(TaxIncompleteReason.OpeningNotValued);
        }

        if (closing.Status == TaxValueStatus.NotValued)
        {
            reasons.Add(TaxIncompleteReason.ClosingNotValued);
        }

        if (events.Any(e => e.Status is TaxAmountStatus.NoRate or TaxAmountStatus.StaleRate or TaxAmountStatus.NoPrice or TaxAmountStatus.StalePrice))
        {
            reasons.Add(TaxIncompleteReason.AmountMissing);
        }

        if (opening.Quantity < 0m || events.Any(e => e.QuantityAfter < 0m))
        {
            reasons.Add(TaxIncompleteReason.NegativeQuantity);
        }

        if (!hasComparable)
        {
            reasons.Add(TaxIncompleteReason.NoComparableQuantity);
        }

        return reasons;
    }

    /// <summary>R28, R29: each notice of the table that applies, once, in the order of the table.</summary>
    private static List<TaxNotice> Notices(TaxTreatment treatment, IReadOnlyList<TaxQuantity> opening, IReadOnlyList<TaxYearEvent> events)
    {
        var notices = new SortedSet<TaxNotice>();
        if (opening.Any(q => q.CustodyMode == CustodyMode.External)
            || events.Any(e => e.Event.CustodyMode == CustodyMode.External || e.Event.Changes.Any(c => c.CustodyMode == CustodyMode.External)))
        {
            notices.Add(TaxNotice.ExternalFigures);
        }

        foreach (var taxEvent in events.Select(e => e.Event))
        {
            switch (taxEvent.Kind)
            {
                case TaxEventKind.Arrival:
                    notices.Add(TaxNotice.ArrivalAtMarketValue);
                    break;
                case TaxEventKind.Departure:
                    notices.Add(TaxNotice.DepartureAtMarketValue);
                    break;
                case TaxEventKind.Move:
                    notices.Add(TaxNotice.Move);
                    break;
                case TaxEventKind.Reorganisation when taxEvent.LedgerType == TransactionType.Scrip:
                    notices.Add(TaxNotice.Scrip);
                    break;
                case TaxEventKind.Correction:
                    notices.Add(TaxNotice.Correction);
                    break;
                case TaxEventKind.Dividend when Disagrees(taxEvent.Dividend!.TaxSource, treatment.IssuerCountry):
                    notices.Add(TaxNotice.DividendTaxSourceDisagrees);
                    break;
            }
        }

        return notices.ToList();
    }

    /// <summary>R29: <c>NewZealand</c> from an issuer outside New Zealand, or <c>Foreign</c> from one in it. An Instrument
    /// with no issuer country never disagrees.</summary>
    private static bool Disagrees(DividendTaxSource source, string? issuerCountry) =>
        issuerCountry is { } country
        && (source == DividendTaxSource.NewZealand) != string.Equals(country.Trim(), "NZ", StringComparison.OrdinalIgnoreCase);

    private static List<TaxQuantity> Of(IReadOnlyList<TaxQuantity> quantities, int instrumentId) =>
        quantities.Where(quantity => quantity.InstrumentId == instrumentId).ToList();

    private static List<TaxValueLine> Lines(IReadOnlyList<TaxValueLine> lines, int instrumentId) =>
        lines.Where(line => line.Line.InstrumentId == instrumentId).ToList();

    /// <summary>
    /// R22, R23: the quantity at the instant, summed over the Accounts, and its value: the sum of one line per Account
    /// that holds a quantity, when every line is valued. A line that was not given is <c>NoPrice</c>. A quantity of 0 is
    /// worth 0.00 with no line. A treatment that needs no value reads no line.
    /// </summary>
    private static TaxPoint Point(
        DateTimeOffset at, DateOnly day, IReadOnlyList<TaxQuantity> quantities, IReadOnlyList<TaxValueLine> given, bool needsValue)
    {
        var quantity = quantities.Sum(q => q.Quantity);
        if (!needsValue)
        {
            return new TaxPoint(at, day, quantity, null, null, TaxValueStatus.NotRequired, []);
        }

        var lines = quantities
            .Select(q => given.FirstOrDefault(line => line.AccountId == q.AccountId)
                         ?? new TaxValueLine(
                             q.AccountId,
                             q.CustodyMode,
                             new ValuationLine(q.InstrumentId, q.Quantity, q.Quantity, ValuationLineStatus.NoPrice, null, null, null, null)))
            .ToList();
        return lines.All(line => line.Line.Status == ValuationLineStatus.Valued)
            ? new TaxPoint(at, day, quantity, null, lines.Sum(line => line.Line.Value!.Value), TaxValueStatus.Valued, lines)
            : new TaxPoint(at, day, quantity, null, null, TaxValueStatus.NotValued, lines);
    }

    /// <summary>R10–R14, R18, R21: the NZD figures of one event.</summary>
    private static TaxYearEvent Amounts(
        int sequence,
        TaxEvent taxEvent,
        IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates,
        TaxYearValues values,
        int decimalPlaces)
    {
        var none = new TaxYearEvent(sequence, taxEvent, 0m, null, null, TaxAmountStatus.None, null, null, null, null, null);
        switch (taxEvent.Kind)
        {
            case TaxEventKind.Arrival:
            case TaxEventKind.Departure:
                return MarketValue(none, values.Events.GetValueOrDefault(taxEvent.Ordinal));

            case TaxEventKind.Acquisition:
            case TaxEventKind.Disposal:
            case TaxEventKind.Dividend:
                var currency = taxEvent.Trade?.Currency ?? taxEvent.Dividend!.Currency;
                var (status, rate) = RateOf(currency, LedgerDay.Of(taxEvent.BookedAt), rates);
                decimal? Nzd(decimal amount) => status switch
                {
                    TaxAmountStatus.Stated => amount,
                    TaxAmountStatus.Converted => Round(amount * rate!.Rate, decimalPlaces),
                    _ => null
                };

                if (taxEvent.Dividend is { } dividend)
                {
                    var gross = Nzd(dividend.Gross);
                    return none with
                    {
                        Status = status,
                        Fx = rate,
                        Amount = gross,
                        Dividend = new TaxDividendAmounts(
                            dividend.Shares,
                            dividend.TaxSource,
                            gross,
                            Nzd(dividend.ForeignTax),
                            Nzd(dividend.Rwt),
                            Nzd(dividend.Nrwt),
                            Nzd(dividend.ImputationCredit))
                    };
                }

                var trade = taxEvent.Trade!;
                var principal = Nzd(trade.Principal);
                var fee = Nzd(trade.Fee);
                // R10: an acquisition costs the principal and the fee; a disposal brings the principal less the fee.
                var amount = taxEvent.Kind == TaxEventKind.Disposal ? principal - fee : principal + fee;
                return none with { Status = status, Fx = rate, Principal = principal, Fee = fee, Amount = amount };

            default:
                return none;
        }
    }

    /// <summary>R14: the value of the line of an arrival or a departure, or the status of a line that has none.</summary>
    private static TaxYearEvent MarketValue(TaxYearEvent none, ValuationLine? line)
    {
        if (line is null)
        {
            return none with { Status = TaxAmountStatus.NoPrice };
        }

        var status = line.Status switch
        {
            ValuationLineStatus.Valued => TaxAmountStatus.MarketValue,
            ValuationLineStatus.StalePrice => TaxAmountStatus.StalePrice,
            ValuationLineStatus.NoRate => TaxAmountStatus.NoRate,
            ValuationLineStatus.StaleRate => TaxAmountStatus.StaleRate,
            _ => TaxAmountStatus.NoPrice
        };
        return none with { Status = status, Amount = line.Value, Price = line.Price, Fx = line.Fx };
    }

    /// <summary>R18, R21: an NZD leg is used as booked; any other needs a row of the pair no older than the age limit.</summary>
    private static (TaxAmountStatus Status, StoredRate? Rate) RateOf(
        string currency, DateOnly date, IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates)
    {
        if (currency == Nzd)
        {
            return (TaxAmountStatus.Stated, null);
        }

        if (!rates.TryGetValue((currency, date), out var rate))
        {
            return (TaxAmountStatus.NoRate, null);
        }

        return ValuationRules.IsFresh(rate.AsOfDate, date) ? (TaxAmountStatus.Converted, rate) : (TaxAmountStatus.StaleRate, rate);
    }

    /// <summary>R23: an opening and a closing value are worked out for an attributing and a not classified holding.</summary>
    public static bool NeedsValue(FifTreatment treatment) =>
        treatment is FifTreatment.Attributing or FifTreatment.AttributingNoFdr or FifTreatment.NotClassified;

    /// <summary>The currency of every figure of a tax year (R31).</summary>
    public const string Nzd = "NZD";

    private static decimal Round(decimal amount, int places) => decimal.Round(amount, places, MidpointRounding.AwayFromZero);
}

/// <summary>R26: the treatment of an Instrument for the year, with what a report has to keep.</summary>
public sealed record TaxTreatment(FifTreatment FifTreatment, int? RowTaxYear, string? Note, string? IssuerCountry)
{
    public static TaxTreatment NotClassified(string? issuerCountry = null) => new(FifTreatment.NotClassified, null, null, issuerCountry);
}

/// <summary>
/// The valuation lines <see cref="CustomerTaxYear.Compute"/> takes (R14, R22): the lines of the opening and of the
/// closing, one per Account and Instrument that needs a value, and the line of each arrival or departure of the year by
/// its <see cref="TaxEvent.Ordinal"/>.
/// </summary>
public sealed record TaxYearValues(
    IReadOnlyList<TaxValueLine> Opening,
    IReadOnlyList<TaxValueLine> Closing,
    IReadOnlyDictionary<int, ValuationLine> Events)
{
    public static TaxYearValues None { get; } = new([], [], new Dictionary<int, ValuationLine>());
}

/// <summary>One line of an opening or a closing: an Account's quantity of the Instrument, valued in NZD.</summary>
public sealed record TaxValueLine(int AccountId, CustodyMode CustodyMode, ValuationLine Line);

/// <summary>One Instrument as the Customer holds it over all their Accounts, in one tax year (R5, R31).</summary>
public sealed record TaxHolding(
    int InstrumentId,
    TaxTreatment Treatment,
    TaxPoint Opening,
    TaxPoint Closing,
    decimal? PeakQuantity,
    IReadOnlyList<TaxYearEvent> Events,
    IReadOnlyList<TaxIncompleteReason> Incomplete,
    IReadOnlyList<TaxNotice> Notices)
{
    /// <summary>R27: complete if and only if no reason is listed.</summary>
    public bool Complete => Incomplete.Count == 0;
}

/// <summary>The opening or the closing of a tax holding (R22, R23).</summary>
public sealed record TaxPoint(
    DateTimeOffset At,
    DateOnly ValuationDay,
    decimal Quantity,
    decimal? ComparableQuantity,
    decimal? Value,
    TaxValueStatus Status,
    IReadOnlyList<TaxValueLine> Lines);

/// <summary>A tax event of the year with its NZD figures and the quantities after it (R18–R25, R31).</summary>
public sealed record TaxYearEvent(
    int Sequence,
    TaxEvent Event,
    decimal QuantityAfter,
    decimal? ComparableQuantityAfter,
    decimal? Amount,
    TaxAmountStatus Status,
    StoredRate? Fx,
    StoredPrice? Price,
    decimal? Principal,
    decimal? Fee,
    TaxDividendAmounts? Dividend);

/// <summary>R11: the figures of a dividend in NZD, each <c>null</c> when it could not be worked out.</summary>
public sealed record TaxDividendAmounts(
    decimal Shares,
    DividendTaxSource TaxSource,
    decimal? Gross,
    decimal? ForeignTax,
    decimal? Rwt,
    decimal? Nrwt,
    decimal? ImputationCredit);
