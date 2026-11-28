using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-cost-threshold R5–R14: the tax cost of each tax holding, in NZD, after each tax event, from the Customer's first
/// event on. Reads no clock and no store.
/// </summary>
public static class TaxCostLedger
{
    /// <summary>
    /// R14: the currencies and dates whose rate row into NZD the walk of <paramref name="costed"/> reads: an acquisition
    /// in another currency on the UTC date of its <c>bookedAt</c>; a carried cost of an arrival in another currency on
    /// its <c>acquiredOn</c>, else on that UTC date; a correction's cost change in another currency on that UTC date.
    /// </summary>
    public static IReadOnlySet<(string Currency, DateOnly Date)> RateKeys(IEnumerable<TaxEvent> events, IReadOnlySet<int> costed)
    {
        var keys = new HashSet<(string Currency, DateOnly Date)>();
        foreach (var taxEvent in events.Where(e => costed.Contains(e.InstrumentId)))
        {
            (string Currency, DateOnly Date)? key = taxEvent.Kind switch
            {
                TaxEventKind.Acquisition => (taxEvent.Trade!.Currency, LedgerDay.Of(taxEvent.BookedAt)),
                TaxEventKind.Arrival or TaxEventKind.Correction when taxEvent.CarriedCost!.Amount != 0m =>
                    (taxEvent.CarriedCost.Currency, RateDate(taxEvent)),
                _ => null
            };
            if (key is { Currency: { Length: > 0 } currency } found && currency != CustomerTaxYear.Nzd)
            {
                keys.Add(found);
            }
        }

        return keys;
    }

    /// <summary>
    /// R5–R14: walks <paramref name="events"/> (the order of <see cref="TaxLedger.Build"/>) for the Instruments in
    /// <paramref name="costed"/>. <paramref name="rates"/> holds the rate row into NZD read for a currency and a date
    /// (none: no row of the pair); <paramref name="decimalPlaces"/> are NZD's.
    /// </summary>
    public static TaxCostWalk Walk(
        IReadOnlyList<TaxEvent> events,
        IReadOnlySet<int> costed,
        IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates,
        int decimalPlaces)
    {
        var steps = new List<TaxCostStep>();
        var states = new Dictionary<int, TaxCostState>();
        foreach (var taxEvent in events.Where(e => costed.Contains(e.InstrumentId)))
        {
            var before = states.GetValueOrDefault(taxEvent.InstrumentId) ?? new TaxCostState(0m, TaxFx.Zero(decimalPlaces), null);
            var step = Next(taxEvent, before, rates, decimalPlaces);
            steps.Add(step);
            states[taxEvent.InstrumentId] = new TaxCostState(step.QuantityAfter, step.CostAfter, step.Unknown);
        }

        return new TaxCostWalk(steps);
    }

    /// <summary>One event applied to the state of its tax holding before it (R6–R13).</summary>
    private static TaxCostStep Next(
        TaxEvent taxEvent,
        TaxCostState before,
        IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates,
        int decimalPlaces)
    {
        var quantity = before.Quantity + taxEvent.Changes.Sum(change => change.Quantity);
        var figure = Figure(taxEvent, rates, decimalPlaces);
        decimal? after = null;
        var unknown = before.Unknown;
        if (before.Cost is { } cost)
        {
            var cause = figure?.Cause;
            if (cause is null)
            {
                after = taxEvent.Kind switch
                {
                    // R6, R9, R11: the figure the event adds, signed for a correction.
                    TaxEventKind.Acquisition or TaxEventKind.Arrival or TaxEventKind.Correction => cost + figure!.Value.Amount,
                    // R7, R10: the average of what is held before; more than is held is a negative quantity (below).
                    TaxEventKind.Disposal or TaxEventKind.Departure when quantity >= 0m =>
                        cost - Released(cost, -taxEvent.Quantity, before.Quantity, decimalPlaces),
                    TaxEventKind.Disposal or TaxEventKind.Departure => null,
                    // R8: a reorganisation, a dividend and a move change nothing.
                    _ => cost
                };
                cause = quantity < 0m ? TaxCostUnknownCause.NegativeQuantity
                    : after < 0m ? TaxCostUnknownCause.NegativeCost
                    : null;
            }

            if (cause is { } first)
            {
                after = null;
                unknown = new TaxCostUnknown(first, taxEvent.Ordinal, taxEvent.BookedAt, taxEvent.TransactionPublicId);
            }
        }

        // R12: nothing held costs nothing, whatever was known before.
        if (quantity == 0m)
        {
            after = TaxFx.Zero(decimalPlaces);
            unknown = null;
        }

        return new TaxCostStep(taxEvent, quantity, after - before.Cost, after, figure?.Fx, unknown);
    }

    /// <summary>
    /// R6, R9, R11, R13, R14: the NZD figure an acquisition, an arrival or a correction adds, with the rate row of a
    /// carried cost or a correction, or the cause it cannot be worked out. None for the other kinds. A correction's cost
    /// change of 0 needs no rate.
    /// </summary>
    private static (decimal Amount, StoredRate? Fx, TaxCostUnknownCause? Cause)? Figure(
        TaxEvent taxEvent, IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates, int decimalPlaces)
    {
        switch (taxEvent.Kind)
        {
            case TaxEventKind.Acquisition:
                var trade = taxEvent.Trade!;
                var (status, rate) = TaxFx.RateOf(trade.Currency, LedgerDay.Of(taxEvent.BookedAt), rates);
                if (Unconverted(status) is { } cause)
                {
                    return (0m, null, cause);
                }

                return (TaxFx.Nzd(trade.Principal, status, rate, decimalPlaces)!.Value + TaxFx.Nzd(trade.Fee, status, rate, decimalPlaces)!.Value,
                    null, null);

            case TaxEventKind.Arrival:
            case TaxEventKind.Correction:
                var carried = taxEvent.CarriedCost!;
                if (carried.Amount == 0m)
                {
                    return taxEvent.Kind == TaxEventKind.Arrival && taxEvent.Quantity != 0m
                        ? (0m, null, TaxCostUnknownCause.NoCarriedCost)
                        : (TaxFx.Zero(decimalPlaces), null, null);
                }

                var (carriedStatus, carriedRate) = TaxFx.RateOf(carried.Currency, RateDate(taxEvent), rates);
                return (TaxFx.Nzd(carried.Amount, carriedStatus, carriedRate, decimalPlaces) ?? 0m, carriedRate, Unconverted(carriedStatus));

            default:
                return null;
        }
    }

    /// <summary>R9, R14: the date of a carried cost's rate: <c>acquiredOn</c> when the arrival states one, else the UTC
    /// date of <c>bookedAt</c>.</summary>
    private static DateOnly RateDate(TaxEvent taxEvent) => taxEvent.CarriedCost?.AcquiredOn ?? LedgerDay.Of(taxEvent.BookedAt);

    private static TaxCostUnknownCause? Unconverted(TaxAmountStatus status) => status switch
    {
        TaxAmountStatus.NoRate => TaxCostUnknownCause.NoRate,
        TaxAmountStatus.StaleRate => TaxCostUnknownCause.StaleRate,
        _ => null
    };

    /// <summary>R7: <c>Round(cost × out ÷ before, dp)</c>; everything held taken out removes the whole cost.</summary>
    private static decimal Released(decimal cost, decimal quantityOut, decimal quantityBefore, int decimalPlaces) =>
        quantityOut == quantityBefore ? cost : TaxFx.Round(cost * quantityOut / quantityBefore, decimalPlaces);
}

/// <summary>The steps of the walk, one per event of a costed Instrument, in the order of the events.</summary>
public sealed record TaxCostWalk(IReadOnlyList<TaxCostStep> Steps)
{
    /// <summary>The step of the event with <paramref name="ordinal"/>, or none when its Instrument is not costed.</summary>
    public TaxCostStep? Of(int ordinal) => Steps.FirstOrDefault(step => step.Event.Ordinal == ordinal);

    /// <summary>The quantity and the tax cost of <paramref name="instrumentId"/> from the events booked before
    /// <paramref name="instant"/>: before its first event the quantity is 0 and the cost 0.00.</summary>
    public TaxCostState At(int instrumentId, DateTimeOffset instant) =>
        Steps.LastOrDefault(step => step.Event.InstrumentId == instrumentId && step.Event.BookedAt < instant) is { } step
            ? new TaxCostState(step.QuantityAfter, step.CostAfter, step.Unknown)
            : new TaxCostState(0m, 0m, null);

    /// <summary>
    /// Tax-cost-threshold R22, R25: the instant the quantity of <paramref name="instrumentId"/> last left 0 before
    /// <paramref name="instant"/>, or none when nothing is held at it.
    /// </summary>
    public DateTimeOffset? HeldSince(int instrumentId, DateTimeOffset instant)
    {
        DateTimeOffset? since = null;
        var quantity = 0m;
        foreach (var step in Steps.Where(step => step.Event.InstrumentId == instrumentId && step.Event.BookedAt < instant))
        {
            if (quantity == 0m && step.QuantityAfter != 0m)
            {
                since = step.Event.BookedAt;
            }

            quantity = step.QuantityAfter;
        }

        return quantity == 0m ? null : since;
    }
}

/// <summary>
/// One event of the walk: the quantity of the tax holding after it, summed over the Accounts; the change of the tax
/// cost and the cost after it, both <c>null</c> while it is unknown; the rate row of a carried cost or a correction;
/// and, while the cost is unknown, its first cause.
/// </summary>
public sealed record TaxCostStep(
    TaxEvent Event,
    decimal QuantityAfter,
    decimal? Change,
    decimal? CostAfter,
    StoredRate? Fx,
    TaxCostUnknown? Unknown);

/// <summary>The quantity and the tax cost of a tax holding at an instant; <see cref="Cost"/> is <c>null</c> while unknown.</summary>
public sealed record TaxCostState(decimal Quantity, decimal? Cost, TaxCostUnknown? Unknown);

/// <summary>R13: why a tax cost is unknown, and the event that made it so.</summary>
public sealed record TaxCostUnknown(TaxCostUnknownCause Cause, int Ordinal, DateTimeOffset At, Guid TransactionPublicId);

/// <summary>R13: the causes of an unknown tax cost.</summary>
public enum TaxCostUnknownCause
{
    NoCarriedCost = 0,
    NoRate = 1,
    StaleRate = 2,
    NegativeQuantity = 3,
    NegativeCost = 4
}
