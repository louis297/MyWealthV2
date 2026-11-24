using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>The custodian elections adapter for <c>Adapter = Simulator</c> (dividends R65). Not implemented yet.</summary>
public sealed class SimulatorCustodianElections(IHttpClientFactory httpClients) : ICustodianElectionsAdapter
{
    public const string HttpClientName = "custodian-elections-simulator";

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public Task<CustodianElectionResult> SubmitAsync(
        CustodianConnectionSettings connection, CustodianElectionRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException(httpClients.GetType().Name);
}
