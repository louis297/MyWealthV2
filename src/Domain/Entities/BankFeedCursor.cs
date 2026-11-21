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

    /// <summary>The bank's balance after the last stored line, as the last stored page sent it (bank-reconciliation R3).</summary>
    public decimal? Balance { get; private set; }

    /// <summary>When the page that carried <see cref="Balance"/> was stored.</summary>
    public DateTimeOffset? BalanceAt { get; private set; }

    /// <summary>The money in the trust account before the first line the platform holds; set once (R4).</summary>
    public decimal? OpeningBalance { get; private set; }

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

    /// <summary>
    /// A page was stored with the bank's balance after its last line (bank-reconciliation R3–R5).
    /// <paramref name="signedLinesTotal"/> is the signed sum of every statement line stored for the client money
    /// account so far, the page's included; the first balance that is not null sets the opening balance from it.
    /// </summary>
    public void StorePage(string? cursor, decimal? balance, decimal signedLinesTotal, DateTimeOffset at)
    {
        Advance(cursor, at);
        Balance = balance;
        BalanceAt = balance is null ? null : at;
        if (balance is not null && OpeningBalance is null)
        {
            OpeningBalance = balance.Value - signedLinesTotal;
        }
    }

    /// <summary>A read failed: the cursor stays (R7).</summary>
    public void Fail(BankFeedFailure failure, DateTimeOffset at)
    {
        LastFailure = failure;
        LastFailedAt = at;
    }
}
