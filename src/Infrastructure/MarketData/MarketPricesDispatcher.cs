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
        var settings = connections.Settings.SingleOrDefault(entry => entry.Name == connection);
        var adapter = settings is null ? null : adapters.SingleOrDefault(entry => entry.Adapter == settings.Adapter);
        if (settings is null || adapter is null)
        {
            logger.LogWarning("Market data connection {Connection} has no adapter on this host.", connection);
            return Task.FromResult(MarketPricesResult.Failed(MarketDataFailure.Unreachable));
        }

        return adapter.GetPricesAsync(settings, cancellationToken);
    }
}
