namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The state of one feed of one market data connection (docs/features/simulated-instruments.md §5, §6.1; ADR 0021
/// rule 16): when it was last read and how that went. Platform data: no tenant. The <c>Prices</c> feed has no cursor.
/// </summary>
public class MarketDataFeed
{
    public const int ConnectionMaxLength = 50;
    public const int CursorMaxLength = 500;

    private MarketDataFeed()
    {
    }

    public string Connection { get; private set; } = string.Empty;

    public MarketDataFeedKind Kind { get; private set; }

    public string? Cursor { get; private set; }

    public DateTimeOffset? LastSucceededAt { get; private set; }

    public DateTimeOffset? LastFailedAt { get; private set; }

    public MarketDataFailure? LastFailure { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>A feed before its first read: nothing tried yet.</summary>
    public static MarketDataFeed Start(string connection, MarketDataFeedKind kind) =>
        new() { Connection = connection, Kind = kind };

    /// <summary>A read was stored (R9). The last failure stays, so the page can tell which came later.</summary>
    public void Succeeded(DateTimeOffset at)
    {
        LastSucceededAt = at;
    }

    /// <summary>A read failed (R8). The last success stays.</summary>
    public void Failed(MarketDataFailure failure, DateTimeOffset at)
    {
        LastFailure = failure;
        LastFailedAt = at;
    }
}
