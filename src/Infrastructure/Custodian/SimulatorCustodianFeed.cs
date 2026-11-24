using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>The custodian feed adapter for <c>Adapter = Simulator</c> (in-kind-transfers R73).</summary>
public sealed class SimulatorCustodianFeed(IHttpClientFactory httpClients) : ICustodianFeedAdapter
{
    public const string HttpClientName = "custodian-feed-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public Task<CustodianFeedResult> GetMovementsAsync(
        CustodianConnectionSettings connection, CustodianFeedRequest request, CancellationToken cancellationToken)
    {
        _ = httpClients;
        throw new NotImplementedException();
    }
}
