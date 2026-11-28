using MyWealthV2.Domain.Valuations;

namespace MyWealthV2.Domain.Tax;

/// <summary>
/// Tax-cost-threshold R5–R14: the tax cost of each tax holding, in NZD, after each tax event, from the Customer's first
/// event on. Reads no clock and no store.
/// </summary>
public static class TaxCostLedger
{
    /// <summary>
    /// R5–R14: walks <paramref name="events"/> (the order of <see cref="TaxLedger.Build"/>) for the Instruments in
    /// <paramref name="costed"/>. <paramref name="rates"/> holds the rate row into NZD read for a currency and a date
    /// (none: no row of the pair); <paramref name="decimalPlaces"/> are NZD's.
    /// </summary>
    public static TaxCostWalk Walk(
        IReadOnlyList<TaxEvent> events,
        IReadOnlySet<int> costed,
        IReadOnlyDictionary<(string Currency, DateOnly Date), StoredRate> rates,
        int decimalPlaces) =>
        new([]);
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
