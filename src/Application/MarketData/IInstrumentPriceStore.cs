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
}
