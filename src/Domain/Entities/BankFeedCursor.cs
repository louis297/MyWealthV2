namespace MyWealthV2.Domain.Entities;

/// <summary>
/// Where the statement of one client money account was last read, and how that went (deposit-matching R4, R9;
/// ADR 0021 rules 6, 16). The cursor is the party's opaque string; the platform never parses it.
/// </summary>
public class BankFeedCursor
{
    public const int CursorMaxLength = 500;

    private BankFeedCursor()
    {
    }

    public int ClientMoneyAccountId { get; private set; }

    public string? Cursor { get; private set; }

    public DateTimeOffset? LastSucceededAt { get; private set; }

    public DateTimeOffset? LastFailedAt { get; private set; }

    public BankFeedFailure? LastFailure { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>A cursor before the first read: no cursor, nothing tried yet.</summary>
    public static BankFeedCursor Start(int clientMoneyAccountId) => new() { ClientMoneyAccountId = clientMoneyAccountId };

    /// <summary>A page was stored: the party's new cursor and the time (R8).</summary>
    public void Advance(string? cursor, DateTimeOffset at)
    {
        if (cursor is { Length: > CursorMaxLength })
        {
            throw new DomainException($"A bank feed cursor is at most {CursorMaxLength} characters.");
        }

        Cursor = cursor;
        LastSucceededAt = at;
    }

    /// <summary>A read failed: the cursor stays (R7).</summary>
    public void Fail(BankFeedFailure failure, DateTimeOffset at)
    {
        LastFailure = failure;
        LastFailedAt = at;
    }
}
