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
/// one range call to the source from the history floor date to yesterday (UTC), stored by the final rule. A base whose
/// call fails stores nothing; the others are still asked, and the run fails after every base was tried.
/// </summary>
public sealed class FxRateHistorySyncJob(
    ICurrencyCatalog catalog,
    IFxRateSource source,
    IFxRateStore store,
    MarketDataHistorySettings history,
    TimeProvider time,
    ILogger<FxRateHistorySyncJob> logger)
{
    public async Task Sync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var yesterday = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        if (history.FloorDate is not { } floor || floor > yesterday)
        {
            return;
        }

        var codes = catalog.List(enabledOnly: true)
            .Select(currency => currency.Code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var active = codes.ToHashSet(StringComparer.Ordinal);
        var failed = new List<string>();
        foreach (var code in codes)
        {
            var quotes = codes.Where(other => other != code).ToList();
            if (quotes.Count == 0)
            {
                continue;
            }

            try
            {
                var records = await source.GetRangeAsync(code, quotes, floor, yesterday, cancellationToken);
                var rows = Storable(code, records, floor, yesterday, active);
                if (rows.Count > 0)
                {
                    // R15: the rows of one base in one write.
                    await store.UpsertAsync(rows, now, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "FX rate history sync failed for base {BaseCurrency}.", code);
                failed.Add(code);
            }
        }

        if (failed.Count > 0)
        {
            throw new FxRateHistorySyncFailedException(failed);
        }
    }

    /// <summary>R14: the records of the answer the job stores.</summary>
    private List<FxRateQuote> Storable(
        string baseCurrency,
        IReadOnlyList<FxRateQuote> records,
        DateOnly floor,
        DateOnly yesterday,
        IReadOnlySet<string> active)
    {
        var rows = new List<FxRateQuote>();
        foreach (var record in records)
        {
            var recordBase = record.BaseCurrency.Trim().ToUpperInvariant();
            var recordQuote = record.QuoteCurrency.Trim().ToUpperInvariant();
            if (record.AsOfDate < floor || record.AsOfDate > yesterday || recordBase == recordQuote)
            {
                continue;
            }

            if (!active.Contains(recordBase) || !active.Contains(recordQuote))
            {
                logger.LogWarning(
                    "FX rate history for base {BaseCurrency} sent {RecordBase}/{RecordQuote}, a currency that is not active; skipped.",
                    baseCurrency, recordBase, recordQuote);
                continue;
            }

            if (record.Rate <= 0m)
            {
                logger.LogWarning(
                    "FX rate history sent {RecordBase}/{RecordQuote} on {AsOfDate} with a rate that is not above zero; skipped.",
                    recordBase, recordQuote, record.AsOfDate);
                continue;
            }

            rows.Add(record with { BaseCurrency = recordBase, QuoteCurrency = recordQuote });
        }

        return rows;
    }
}
