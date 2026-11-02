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
        try
        {
            var codes = ActiveCodes();
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            if (await store.HasAsOfDateForEveryPairAsync(codes, today, cancellationToken))
            {
                return;
            }

            var collected = new List<FxRateQuote>();
            foreach (var code in codes)
            {
                var quotes = codes.Where(other => other != code).ToList();
                if (quotes.Count == 0)
                {
                    continue;
                }

                collected.AddRange(await source.GetLatestAsync(code, quotes, cancellationToken));
            }

            if (collected.Count > 0)
            {
                await store.UpsertAsync(collected, time.GetUtcNow(), cancellationToken);
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

}
