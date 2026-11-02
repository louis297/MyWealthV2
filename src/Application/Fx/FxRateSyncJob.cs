using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Fx;

public sealed class FxRateSyncJob(
    ICurrencyCatalog catalog,
    IFxRateSource source,
    IFxRateStore store,
    TimeProvider time,
    ILogger<FxRateSyncJob> logger)
{
    public Task Sync(CancellationToken cancellationToken = default)
    {
        _ = catalog;
        _ = source;
        _ = store;
        _ = time;
        _ = logger;
        _ = cancellationToken;
        throw new NotImplementedException();
    }
}
