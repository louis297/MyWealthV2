using MyWealthV2.Application.Broker;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>
/// The broker feed adapter for <c>Adapter = Simulator</c> (docs/features/orders.md R63, R67).
/// </summary>
public sealed class SimulatorBrokerFeed : IBrokerFeedAdapter
{
    public const string HttpClientName = "broker-feed-simulator";

    public SimulatorBrokerFeed(IHttpClientFactory httpClients)
    {
        _ = httpClients;
    }

    public string Adapter => BrokerConnectionsConfiguration.SimulatorAdapter;

    public Task<BrokerFeedResult> GetReportsAsync(BrokerConnectionSettings connection, string? cursor, int limit, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
