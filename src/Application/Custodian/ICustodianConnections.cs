namespace MyWealthV2.Application.Custodian;

/// <summary>A configured custodian connection as the platform may show it: its name and adapter, never its URL or key
/// (docs/features/in-kind-transfers.md R2, R7).</summary>
public sealed record CustodianConnectionInfo(string Name, string Adapter);

/// <summary>The custodian connections configured on this host (in-kind-transfers R2).</summary>
public interface ICustodianConnections
{
    /// <summary>Every configured connection, sorted by name.</summary>
    IReadOnlyList<CustodianConnectionInfo> All { get; }

    /// <summary>True when <paramref name="name"/> is a connection configured on this host.</summary>
    bool IsConfigured(string? name);
}
