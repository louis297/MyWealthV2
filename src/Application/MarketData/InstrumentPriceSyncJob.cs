using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Application.MarketData;

public sealed class InstrumentPriceSyncJob(
    IActiveInstrumentCatalog catalog,
    IInstrumentPriceSource source,
    IInstrumentPriceStore store,
    MarketDataConnections connections,
    MarketDataHistorySettings history,
    TimeProvider time,
    TimeSpan requestSpacing,
    ILogger<InstrumentPriceSyncJob> logger)
{
    public async Task Sync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Simulated instruments R12: an instrument a market data connection serves is priced by it, never here.
            var active = (await catalog.ListActiveAsync(cancellationToken))
                .Where(instrument => connections.For(instrument.Symbol) is null)
                .ToList();
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            _ = history;
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
                var sourced = await source.GetBarsAsync(group.Key.Symbol, group.Key.Quote, null, cancellationToken);
                if (sourced is null)
                {
                    logger.LogWarning(
                        "Instrument price source rejected {Symbol} {QuoteCurrency}.",
                        group.Key.Symbol,
                        group.Key.Quote);
                    continue;
                }

                if (!string.Equals(sourced.Currency.Trim(), group.Key.Quote, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Instrument price currency {Currency} does not match {QuoteCurrency} for {Symbol}.",
                        sourced.Currency,
                        group.Key.Quote,
                        group.Key.Symbol);
                    continue;
                }

                var latest = sourced.Bars.OrderByDescending(bar => bar.AsOfDate).FirstOrDefault();
                if (latest.Close is not { } close)
                {
                    continue;
                }

                foreach (var instrument in group)
                {
                    collected.Add(new InstrumentPriceQuote(instrument.Id, close, latest.AsOfDate));
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
