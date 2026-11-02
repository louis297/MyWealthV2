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
    public async Task Sync(CancellationToken cancellationToken = default)
    {
        _ = store;
        _ = time;
        try
        {
            foreach (var code in ActiveCodes())
            {
                var quotes = OtherCodes(code);
                if (quotes.Count == 0)
                {
                    continue;
                }

                await source.GetLatestAsync(code, quotes, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "FX rate sync failed.");
        }
    }

    private List<string> ActiveCodes() =>
        catalog.List(enabledOnly: true)
            .Select(currency => currency.Code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private List<string> OtherCodes(string code) =>
        ActiveCodes().Where(other => other != code).ToList();
}
