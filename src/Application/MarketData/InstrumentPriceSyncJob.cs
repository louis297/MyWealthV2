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
        try
        {
            var active = await catalog.ListActiveAsync(cancellationToken);
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            if (await store.HasAsOfDateForEveryActiveInstrumentAsync(
                    active.Select(instrument => instrument.Id).ToList(),
                    today,
                    cancellationToken))
            {
                return;
            }

            var groups = active
                .GroupBy(instrument => (
                    Symbol: instrument.Symbol.Trim().ToUpperInvariant(),
                    Quote: instrument.QuoteCurrency.Trim().ToUpperInvariant()))
                .OrderBy(group => group.Key.Symbol, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Quote, StringComparer.Ordinal)
                .ToList();

            var collected = new List<InstrumentPriceQuote>();
            for (var index = 0; index < groups.Count; index++)
            {
                if (index > 0 && requestSpacing > TimeSpan.Zero)
                {
                    await Task.Delay(requestSpacing, cancellationToken);
                }

                var group = groups[index];
                var sourced = await source.GetLatestAsync(group.Key.Symbol, group.Key.Quote, cancellationToken);
                if (sourced is null)
                {
                    logger.LogWarning(
                        "Instrument price source rejected {Symbol} {QuoteCurrency}.",
                        group.Key.Symbol,
                        group.Key.Quote);
                    continue;
                }

                if (!string.Equals(sourced.Value.Currency.Trim(), group.Key.Quote, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Instrument price currency {Currency} does not match {QuoteCurrency} for {Symbol}.",
                        sourced.Value.Currency,
                        group.Key.Quote,
                        group.Key.Symbol);
                    continue;
                }

                foreach (var instrument in group)
                {
                    collected.Add(new InstrumentPriceQuote(instrument.Id, sourced.Value.Price, sourced.Value.AsOfDate));
                }
            }

            if (collected.Count > 0)
            {
                await store.UpsertAsync(collected, time.GetUtcNow(), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Instrument price sync failed.");
        }
    }
}
