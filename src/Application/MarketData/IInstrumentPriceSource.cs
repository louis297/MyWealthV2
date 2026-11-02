namespace MyWealthV2.Application.MarketData;

public interface IInstrumentPriceSource
{
    Task<SourcedInstrumentPrice?> GetLatestAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken = default);
}
