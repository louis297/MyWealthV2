using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Infrastructure.BankFeed;

/// <summary>
/// The bank feed adapter for <c>Adapter = Simulator</c> (deposit-matching R49): calls the simulator bank's party
/// route (R44) with the connection's key and timeout. It keeps its own wire types (ADR 0021, no shared contracts).
/// </summary>
public sealed class SimulatorBankFeed(IHttpClientFactory httpClients) : IBankFeedAdapter
{
    public const string HttpClientName = "bank-feed-simulator";
    public const string ApiKeyHeader = "X-Api-Key";

    public string Adapter => BankConnectionsConfiguration.SimulatorAdapter;

    public Task<BankFeedResult> GetStatementLinesAsync(
        BankConnectionSettings connection,
        BankFeedRequest request,
        CancellationToken cancellationToken) => throw new NotImplementedException(httpClients.ToString());
}
