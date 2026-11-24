using MyWealthV2.Application.Custodian;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>The functional host's custodian connections: one connection, <c>simulator</c>, unless a test removes it
/// (in-kind-transfers R2, §9.1).</summary>
internal sealed class FakeCustodianConnections : ICustodianConnections
{
    private static readonly IReadOnlyList<CustodianConnectionInfo> Simulator = [new(SafekeepingHttp.Connection, "Simulator")];

    private volatile IReadOnlyList<CustodianConnectionInfo> _all = Simulator;

    public IReadOnlyList<CustodianConnectionInfo> All => _all;

    public bool IsConfigured(string? name) => _all.Any(entry => entry.Name == name);

    /// <summary>No connection configured, until <see cref="Reset"/>.</summary>
    public void RemoveAll() => _all = [];

    public void Reset() => _all = Simulator;
}
