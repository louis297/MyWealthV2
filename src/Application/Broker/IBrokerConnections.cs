namespace MyWealthV2.Application.Broker;

/// <summary>A configured broker connection as the platform may show it: its name and adapter, never its URL or key
/// (docs/features/orders.md R2, R4).</summary>
public sealed record BrokerConnectionInfo(string Name, string Adapter);

/// <summary>The broker connections configured on this host (orders R2).</summary>
public interface IBrokerConnections
{
    /// <summary>Every configured connection, sorted by name.</summary>
    IReadOnlyList<BrokerConnectionInfo> All { get; }

    /// <summary>True when <paramref name="name"/> is a connection configured on this host.</summary>
    bool IsConfigured(string? name);
}
