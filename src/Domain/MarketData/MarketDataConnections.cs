namespace MyWealthV2.Domain.MarketData;

/// <summary>
/// The market data connections configured on this host (docs/features/simulated-instruments.md R2, R4). No two have
/// the same suffix, so an instrument is served by at most one.
/// </summary>
public sealed class MarketDataConnections
{
    public MarketDataConnections(IEnumerable<MarketDataConnection> connections)
    {
        All = connections.OrderBy(connection => connection.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Every configured connection, sorted by name.</summary>
    public IReadOnlyList<MarketDataConnection> All { get; }

    /// <summary>No connection configured: every instrument is priced by EODHD.</summary>
    public static MarketDataConnections None { get; } = new([]);

    /// <summary>R4: the connection that serves <paramref name="symbol"/>, or <c>null</c>.</summary>
    public MarketDataConnection? For(string? symbol) => null;

    /// <summary>The connection with that name, or <c>null</c>.</summary>
    public MarketDataConnection? Named(string? name) => null;
}
