using MyWealthV2.Application.Broker;

namespace MyWealthV2.Infrastructure.Broker;

/// <summary>
/// The broker orders adapter for <c>Adapter = Simulator</c> (docs/features/orders.md R53, R59, R66).
/// </summary>
public sealed class SimulatorBrokerOrders : IBrokerOrdersAdapter
{
    public const string HttpClientName = "broker-orders-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public SimulatorBrokerOrders(IHttpClientFactory httpClients)
    {
        _ = httpClients;
    }

    public string Adapter => BrokerConnectionsConfiguration.SimulatorAdapter;

    public Task<BrokerPlaceResult> PlaceAsync(BrokerConnectionSettings connection, BrokerOrderRequest order, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<BrokerCancelResult> CancelAsync(BrokerConnectionSettings connection, string clientOrderId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
