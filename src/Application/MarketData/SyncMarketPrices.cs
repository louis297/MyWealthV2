using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;

namespace MyWealthV2.Application.MarketData;

/// <summary>What a run of one connection did: the failure of the call, if any, and how many price rows it wrote.</summary>
public sealed record MarketPricesOutcome(MarketDataFailure? Failure, int Stored)
{
    public bool Failed => Failure is not null;
}

/// <summary>
/// Reads the prices of one market data connection through <see cref="IMarketPrices"/> and stores one row per active
/// served instrument, in any Tenant, under the platform's UTC date (docs/features/simulated-instruments.md R8–R11,
/// R13; ADR 0021 rules 11, 15, 16). Runs as the worker's system user and reads across tenants explicitly.
/// </summary>
public sealed class SyncMarketPrices(
    IApplicationDbContext db,
    IMarketPrices prices,
    IActiveInstrumentCatalog catalog,
    IInstrumentPriceStore store,
    MarketDataConnections connections,
    TimeProvider time,
    ILogger<SyncMarketPrices> logger)
{
    public const int SymbolMaxLength = 32;
    public const int PriceMaxDecimals = 8;

    public async Task<MarketPricesOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        var configured = connections.Named(connection);
        if (configured is null)
        {
            logger.LogWarning("Market data connection {Connection} is not configured on this host; nothing is read.", connection);
            return new MarketPricesOutcome(null, 0);
        }

        var result = await prices.GetPricesAsync(connection, cancellationToken);
        var now = time.GetUtcNow();
        if (result.Failure is { } failure)
        {
            // R8: nothing is stored; the prices already stored stay.
            var failed = await FeedAsync(connection, cancellationToken);
            failed.Failed(failure, now);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Market price read failed: connection {Connection}, failure {Failure}.", connection, failure);
            return new MarketPricesOutcome(failure, 0);
        }

        var items = Readable(configured, result.Items ?? []);
        var served = (await catalog.ListActiveAsync(cancellationToken))
            .Where(instrument => configured.Serves(instrument.Symbol))
            .ToList();

        // R10: the platform's UTC date, never the party's.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var quotes = new List<InstrumentPriceQuote>();
        foreach (var instrument in served)
        {
            var sameSymbol = items
                .Where(item => string.Equals(item.Symbol, instrument.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameSymbol.Count == 0)
            {
                logger.LogWarning(
                    "Market data connection {Connection} sent no price for {Symbol}; instrument {InstrumentId} keeps its last price.",
                    connection, instrument.Symbol, instrument.Id);
                continue;
            }

            var item = sameSymbol.FirstOrDefault(candidate =>
                string.Equals(candidate.Currency, instrument.QuoteCurrency, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                logger.LogWarning(
                    "Market data connection {Connection} priced {Symbol} in {Currency}, not in the quote currency {QuoteCurrency} of instrument {InstrumentId}; skipped.",
                    connection, instrument.Symbol, sameSymbol[0].Currency, instrument.QuoteCurrency, instrument.Id);
                continue;
            }

            quotes.Add(new InstrumentPriceQuote(instrument.Id, item.Price!.Value, today));
        }

        // R9: every row of the run and the success time in one transaction.
        await db.ExecuteInTransactionAsync(async token =>
        {
            await store.UpsertAsync(quotes, now, configured.Adapter, token);
            var feed = await FeedAsync(connection, token);
            feed.Succeeded(now);
            await db.SaveChangesAsync(token);
        }, cancellationToken);

        return new MarketPricesOutcome(null, quotes.Count);
    }

    /// <summary>R11: the items whose symbol ends with the suffix and whose fields can be stored. Every other item
    /// with the suffix is skipped with a warning; an item without it is not this connection's.</summary>
    private List<MarketPriceItem> Readable(MarketDataConnection connection, IReadOnlyList<MarketPriceItem> items)
    {
        var readable = new List<MarketPriceItem>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Symbol) || item.Symbol.Length > SymbolMaxLength)
            {
                logger.LogWarning(
                    "Market data connection {Connection} sent a price with a symbol that is empty or longer than {MaxLength} characters; skipped.",
                    connection.Name, SymbolMaxLength);
                continue;
            }

            if (!connection.Serves(item.Symbol))
            {
                continue;
            }

            if (item.Currency is not { Length: 3 } currency || !currency.All(char.IsAsciiLetter))
            {
                logger.LogWarning(
                    "Market data connection {Connection} sent {Symbol} with a currency that is not three letters; skipped.",
                    connection.Name, item.Symbol);
                continue;
            }

            if (item.Price is not { } price || price <= 0 || decimal.Round(price, PriceMaxDecimals) != price)
            {
                logger.LogWarning(
                    "Market data connection {Connection} sent {Symbol} with a price that is not above zero with at most {MaxDecimals} decimal places; skipped.",
                    connection.Name, item.Symbol, PriceMaxDecimals);
                continue;
            }

            readable.Add(item);
        }

        return readable;
    }

    private async Task<MarketDataFeed> FeedAsync(string connection, CancellationToken cancellationToken)
    {
        var feed = await db.MarketDataFeeds
            .SingleOrDefaultAsync(row => row.Connection == connection && row.Kind == MarketDataFeedKind.Prices, cancellationToken);
        if (feed is null)
        {
            feed = MarketDataFeed.Start(connection, MarketDataFeedKind.Prices);
            db.MarketDataFeeds.Add(feed);
        }

        return feed;
    }
}
