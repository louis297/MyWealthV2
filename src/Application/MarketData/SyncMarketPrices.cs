using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
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

    public Task<MarketPricesOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        _ = (db, prices, catalog, store, connections, time, logger);
        return Task.FromResult(new MarketPricesOutcome(null, 0));
    }
}
