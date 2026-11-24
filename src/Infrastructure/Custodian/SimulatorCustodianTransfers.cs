using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Infrastructure.Custodian;

/// <summary>The custodian transfers adapter for <c>Adapter = Simulator</c> (in-kind-transfers R74).</summary>
public sealed class SimulatorCustodianTransfers(IHttpClientFactory httpClients) : ICustodianTransfersAdapter
{
    public const string HttpClientName = "custodian-transfers-simulator";

    public string Adapter => CustodianConnectionsConfiguration.SimulatorAdapter;

    public Task<CustodianTransferResult> SendAsync(
        CustodianConnectionSettings connection, CustodianTransferRequest request, CancellationToken cancellationToken)
    {
        _ = httpClients;
        throw new NotImplementedException();
    }
}
