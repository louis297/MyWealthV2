using MyWealthV2.Application.Broker;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>The functional host's broker connections: one connection, <c>simulator</c> (orders §9.1).</summary>
internal sealed class FakeBrokerConnections : IBrokerConnections
{
    public const string Simulator = "simulator";

    public IReadOnlyList<BrokerConnectionInfo> All { get; } = [new(Simulator, "Simulator")];

    public bool IsConfigured(string? name) => All.Any(entry => entry.Name == name);
}
