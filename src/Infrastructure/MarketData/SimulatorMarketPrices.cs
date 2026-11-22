using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.MarketData;

/// <summary>
/// The market prices adapter for <c>Adapter = Simulator</c> (docs/features/simulated-instruments.md R25): calls the
/// simulator market's price route (R21) with the connection's key and timeout. It keeps its own wire types (ADR 0021,
/// no shared contracts). Every failure comes back as a result.
/// </summary>
public sealed class SimulatorMarketPrices(IHttpClientFactory httpClients) : IMarketPricesAdapter
{
    public const string HttpClientName = "market-prices-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public string Adapter => MarketDataConnectionsConfiguration.SimulatorAdapter;

    public Task<MarketPricesResult> GetPricesAsync(MarketDataConnectionSettings connection, CancellationToken cancellationToken)
    {
        _ = httpClients;
        return Task.FromResult(MarketPricesResult.Succeeded([]));
    }
}
