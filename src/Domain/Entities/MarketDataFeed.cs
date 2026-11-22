namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The state of one feed of one market data connection (docs/features/simulated-instruments.md §5, §6.1; ADR 0021
/// rule 16): when it was last read and how that went. Platform data: no tenant. The <c>Prices</c> feed has no cursor;
/// the <c>CorporateActions</c> feed keeps the party's cursor (corporate-actions R9).
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

    /// <summary>Corporate-actions R9: a page was stored; the cursor moves to the party's new cursor. A missing cursor
    /// keeps the one stored.</summary>
    public void Advance(string? cursor, DateTimeOffset at)
    {
        if (cursor is { Length: > CursorMaxLength })
        {
            throw new DomainException($"A feed cursor is at most {CursorMaxLength} characters.");
        }

        Cursor = cursor ?? Cursor;
        LastSucceededAt = at;
    }

    /// <summary>A read failed (R8). The last success stays.</summary>
    public void Failed(MarketDataFailure failure, DateTimeOffset at)
    {
        LastFailure = failure;
        LastFailedAt = at;
    }
}
