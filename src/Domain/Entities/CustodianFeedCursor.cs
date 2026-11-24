namespace MyWealthV2.Domain.Entities;

/// <summary>
/// Where the custodian feed of one safekeeping account was last read, how that went, and when its positions were
/// stored (in-kind-transfers R7, R10, R12). The cursor is the party's opaque string; the platform never parses it.
/// </summary>
public class CustodianFeedCursor
{
    public const int CursorMaxLength = 500;

    private CustodianFeedCursor()
    {
    }

    public int SafekeepingAccountId { get; private set; }

    public int TenantId { get; private set; }

    public string? Cursor { get; private set; }

    public DateTimeOffset? LastSucceededAt { get; private set; }

    public DateTimeOffset? LastFailedAt { get; private set; }

    public CustodianFailure? LastFailure { get; private set; }

    /// <summary>When the stored positions were written; null until the first page was stored (R12, R54).</summary>
    public DateTimeOffset? PositionsAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>A cursor before the first read: no cursor, nothing tried yet.</summary>
    public static CustodianFeedCursor Start(int tenantId, int safekeepingAccountId) => throw new NotImplementedException();

    /// <summary>R10, R12: a page and its positions were stored at <paramref name="at"/>.</summary>
    public void StorePage(string? cursor, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R10: a read failed; the cursor and the positions stay.</summary>
    public void RecordFailure(CustodianFailure failure, DateTimeOffset at) => throw new NotImplementedException();
}
