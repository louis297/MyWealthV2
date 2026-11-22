namespace MyWealthV2.Domain.Entities;

/// <summary>
/// The state of the execution report feed of one broker connection (docs/features/orders.md R4, R24; ADR 0021
/// rule 16): the party's cursor, and when the feed was last read and how that went. Platform data: no tenant.
/// </summary>
public class BrokerFeed
{
    public const int ConnectionMaxLength = 50;
    public const int CursorMaxLength = 500;

    private BrokerFeed()
    {
    }

    public string Connection { get; private set; } = string.Empty;

    public string? Cursor { get; private set; }

    public DateTimeOffset? LastSucceededAt { get; private set; }

    public DateTimeOffset? LastFailedAt { get; private set; }

    public BrokerFailure? LastFailure { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>A feed before its first read: nothing tried yet.</summary>
    public static BrokerFeed Start(string connection) => throw new NotImplementedException();

    /// <summary>R24: a page was stored; the cursor moves to the party's new cursor. A missing cursor keeps the one
    /// stored. The last failure stays, so a reader can tell which came later.</summary>
    public void Advance(string? cursor, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R24: a read failed. The cursor and the last success stay.</summary>
    public void RecordFailure(BrokerFailure failure, DateTimeOffset at) => throw new NotImplementedException();
}
