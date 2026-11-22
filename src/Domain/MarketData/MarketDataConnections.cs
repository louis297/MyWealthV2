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
        var repeated = All
            .GroupBy(connection => connection.SymbolSuffix, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (repeated is not null)
        {
            throw new DomainException($"The symbol suffix {repeated.Key} is used by more than one market data connection.");
        }
    }

    /// <summary>Every configured connection, sorted by name.</summary>
    public IReadOnlyList<MarketDataConnection> All { get; }

    /// <summary>No connection configured: every instrument is priced by EODHD.</summary>
    public static MarketDataConnections None { get; } = new([]);

    /// <summary>R4: the connection that serves <paramref name="symbol"/>, or <c>null</c>.</summary>
    public MarketDataConnection? For(string? symbol) => All.FirstOrDefault(connection => connection.Serves(symbol));

    /// <summary>The connection with that name, or <c>null</c>.</summary>
    public MarketDataConnection? Named(string? name) =>
        All.FirstOrDefault(connection => string.Equals(connection.Name, name, StringComparison.Ordinal));
}
