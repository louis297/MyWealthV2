namespace MyWealthV2.Application.MarketData;

public interface IInstrumentPriceStore
{
    Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts <paramref name="quotes"/> with <paramref name="source"/> as their <c>Source</c>
    /// (simulated-instruments R9: the adapter name of a market data connection).</summary>
    Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        string source,
        CancellationToken cancellationToken = default);

    Task<bool> HasAsOfDateForEveryActiveInstrumentAsync(
        IReadOnlyList<int> instrumentIds,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    /// <summary>Corporate-actions R23: a price of <paramref name="instrumentId"/> is stored for
    /// <paramref name="from"/> or a later date.</summary>
    Task<bool> HasPriceFromAsync(int instrumentId, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>Orders R10: the date of the price <c>IMarketData.TryGetPrice</c> reads for
    /// <paramref name="onOrBefore"/>: the latest stored price of <paramref name="instrumentId"/> dated on or before
    /// it, or null when there is none.</summary>
    Task<DateOnly?> LatestDateAsync(int instrumentId, DateOnly onOrBefore, CancellationToken cancellationToken = default);
}
