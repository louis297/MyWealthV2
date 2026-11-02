using Microsoft.Extensions.Logging;

namespace MyWealthV2.Application.MarketData;

public sealed class InstrumentPriceSyncJob(
    IActiveInstrumentCatalog catalog,
    IInstrumentPriceSource source,
    IInstrumentPriceStore store,
    TimeProvider time,
    TimeSpan requestSpacing,
    ILogger<InstrumentPriceSyncJob> logger)
{
    public async Task Sync(CancellationToken cancellationToken = default)
    {
        _ = store;
        _ = time;
        _ = requestSpacing;
        _ = logger;
        var active = await catalog.ListActiveAsync(cancellationToken);
        foreach (var instrument in active)
        {
            await source.GetLatestAsync(instrument.Symbol, instrument.QuoteCurrency, cancellationToken);
        }
    }
}
