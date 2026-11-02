namespace MyWealthV2.Application.MarketData;

public interface IInstrumentPriceStore
{
    Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default);
}
