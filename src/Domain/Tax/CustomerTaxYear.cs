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
        var holdings = ofYear
            .GroupBy(e => e.InstrumentId)
            .OrderBy(group => group.Key)
            .Select(group => new TaxHolding(
                group.Key,
                treatments.GetValueOrDefault(group.Key) ?? TaxTreatment.NotClassified(),
                new TaxPoint(from, Tax.TaxYear.OpeningDay(taxYear), 0m, null, null, TaxValueStatus.NotRequired, []),
                new TaxPoint(to, Tax.TaxYear.ClosingDay(taxYear), 0m, null, null, TaxValueStatus.NotRequired, []),
                null,
                group.Select((e, index) => Amounts(index + 1, e, rates, values, decimalPlaces)).ToList(),
                [],
                []))
            .ToList();
        var incomplete = holdings.Count(holding => !holding.Complete);
        return new CustomerTaxYear(taxYear, from, to, holdings, incomplete == 0, incomplete);
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
