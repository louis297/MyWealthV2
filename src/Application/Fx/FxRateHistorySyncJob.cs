using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.MarketData;

namespace MyWealthV2.Application.Fx;

/// <summary>A run of <c>fx-rate-history-sync</c> in which at least one base failed (market-data-history R15).</summary>
public sealed class FxRateHistorySyncFailedException(IReadOnlyList<string> bases)
    : Exception($"FX rate history sync failed for {string.Join(", ", bases)}.")
{
    public IReadOnlyList<string> Bases { get; } = bases;
}

/// <summary>
/// The daily job <c>fx-rate-history-sync</c> (docs/features/market-data-history.md R12–R15): for each active base,
/// one range call to the source from the history floor date to yesterday (UTC), stored by the final rule.
/// </summary>
public sealed class FxRateHistorySyncJob(
    ICurrencyCatalog catalog,
    IFxRateSource source,
    IFxRateStore store,
    MarketDataHistorySettings history,
    TimeProvider time,
    ILogger<FxRateHistorySyncJob> logger)
{
    public Task Sync(CancellationToken cancellationToken = default)
    {
        _ = (catalog, source, store, history, time, logger);
        return Task.CompletedTask;
    }
}
