namespace MyWealthV2.Application.MarketData;

public interface IInstrumentPriceStore
{
    Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default);

    Task<bool> HasAsOfDateForEveryActiveInstrumentAsync(
        IReadOnlyList<int> instrumentIds,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, DateTimeOffset>> LatestFetchedAtByInstrumentAsync(
        IReadOnlyList<int> instrumentIds,
        CancellationToken cancellationToken = default);

    Task NoteFetchedAsync(
        IReadOnlyList<int> instrumentIds,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default);
}
