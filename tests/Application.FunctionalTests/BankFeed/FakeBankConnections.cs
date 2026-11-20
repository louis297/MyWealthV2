using MyWealthV2.Application.BankFeed;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>The functional host's bank connections: one connection, <c>simulator</c> (deposit-matching §9.1).</summary>
internal sealed class FakeBankConnections : IBankConnections
{
    public IReadOnlyList<BankConnectionInfo> All { get; } = [new("simulator", "Simulator")];

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);
}
