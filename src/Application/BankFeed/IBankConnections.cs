namespace MyWealthV2.Application.BankFeed;

/// <summary>A configured bank connection as the platform may show it: its name and adapter, never its URL or key
/// (docs/features/deposit-matching.md R1, R3).</summary>
public sealed record BankConnectionInfo(string Name, string Adapter);

/// <summary>The bank connections configured on this host (deposit-matching R1–R3).</summary>
public interface IBankConnections
{
    /// <summary>Every configured connection, sorted by name.</summary>
    IReadOnlyList<BankConnectionInfo> All { get; }

    /// <summary>True when <paramref name="name"/> is a connection configured on this host.</summary>
    bool IsConfigured(string? name);
}
