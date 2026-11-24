namespace MyWealthV2.Application.Custodian;

/// <summary>The election step of <c>custodian-sync:{connection}</c> (dividends R22). Not implemented yet.</summary>
public sealed class SendReinvestmentElections
{
    public Task<CustodianSendOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
