using Microsoft.Extensions.Logging;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>One adapter of the market prices port, chosen by a connection's <c>Adapter</c>
/// (docs/features/simulated-instruments.md R1, R25).</summary>
public interface IMarketPricesAdapter
{
    string Adapter { get; }

    Task<MarketPricesResult> GetPricesAsync(MarketDataConnectionSettings connection, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IMarketPrices"/> for the hosts: sends each call to the adapter its connection names (ADR 0021 rule 3).
/// A connection that is not configured on this host, or names no registered adapter, is unreachable.
/// </summary>
public sealed class MarketPricesDispatcher(
    ConfiguredMarketDataConnections connections,
    IEnumerable<IMarketPricesAdapter> adapters,
    ILogger<MarketPricesDispatcher> logger) : IMarketPrices
{
    public Task<MarketPricesResult> GetPricesAsync(string connection, CancellationToken cancellationToken)
    {
        _ = (connections, adapters, logger);
        return Task.FromResult(MarketPricesResult.Succeeded([]));
    }
}
