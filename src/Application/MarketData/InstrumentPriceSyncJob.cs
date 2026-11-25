using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Application.MarketData;

/// <summary>
/// The daily EODHD job <c>instrument-price-sync</c> (docs/features/instrument-prices.md, market-data-history.md
/// R7–R11). One call per symbol group. With a history floor date it asks from the first date of the window and stores
/// every bar on or after it; without one it stores the latest bar. The rows of one group are written together.
/// </summary>
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
            if (await store.HasAsOfDateForEveryActiveInstrumentAsync(
                    active.Select(instrument => instrument.Id).ToList(),
                    today,
                    cancellationToken))
            {
                return;
            }

            // Market data history R3: EODHD is asked from the later of the floor date and today minus the days.
            var window = history.FloorDate is { } floor
                ? new MarketDataHistoryWindow(floor, today, history.HistoryDays)
                : null;

            var groups = active
                .GroupBy(instrument => (
                    Symbol: instrument.Symbol.Trim().ToUpperInvariant(),
                    Quote: instrument.QuoteCurrency.Trim().ToUpperInvariant()))
                .OrderBy(group => group.Key.Symbol, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Quote, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < groups.Count; index++)
            {
                if (index > 0 && requestSpacing > TimeSpan.Zero)
                {
                    await Task.Delay(requestSpacing, cancellationToken);
                }

                var group = groups[index];
                var sourced = await source.GetBarsAsync(group.Key.Symbol, group.Key.Quote, window?.From, cancellationToken);
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

                // R7: a bar without a close, or with a close that is not above zero, is skipped. R8: with a window,
                // only the bars inside it.
                var bars = sourced.Bars
                    .Where(bar => bar.Close is > 0m && (window is null || window.Contains(bar.AsOfDate)))
                    .ToList();
                var quotes = group
                    .SelectMany(instrument => bars.Select(bar => new InstrumentPriceQuote(instrument.Id, bar.Close!.Value, bar.AsOfDate)))
                    .ToList();

                // R11: the rows of one symbol group in one write.
                if (quotes.Count > 0)
                {
                    await store.UpsertAsync(quotes, time.GetUtcNow(), cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Instrument price sync failed.");
        }
    }
}
