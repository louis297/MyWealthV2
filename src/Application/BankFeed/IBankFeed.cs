using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// The bank feed port (docs/features/deposit-matching.md §7; ADR 0021 rule 1): one page of a trust account's
/// statement lines after a cursor. Infrastructure dispatches to the adapter of the connection. A failure comes back
/// as a result, never as an exception the caller must parse (R49).
/// </summary>
public interface IBankFeed
{
    Task<BankFeedResult> GetStatementLinesAsync(BankFeedRequest request, CancellationToken cancellationToken);
}

/// <summary>Lines of <paramref name="AccountNumber"/> after <paramref name="Cursor"/> (none: from the start).</summary>
public sealed record BankFeedRequest(string Connection, string AccountNumber, string? Cursor, int Limit);

/// <summary>A line as the bank sent it. Fields the bank left out or sent in a shape the adapter cannot read are
/// null; the pull refuses such a page (R10). On a debit line the payer fields carry the payee. The end-to-end id
/// echoes the instruction id of a payment the platform sent; <paramref name="ReturnOf"/> names the line this line
/// takes back (withdrawals R38, R48).</summary>
public sealed record BankFeedLine(
    string? Id,
    DateOnly? ValueDate,
    BankLineDirection? Direction,
    decimal? Amount,
    string? Currency,
    string? PayerName,
    string? PayerAccount,
    string? Reference,
    string? EndToEndId = null,
    string? ReturnOf = null,
    string? ReturnReason = null);

/// <summary>One page: its lines, oldest first, the party's new cursor, whether more lines follow, and the bank's
/// balance of the account after the last line up to the cursor, or null when the party sends none
/// (bank-reconciliation R2).</summary>
public sealed record BankFeedPage(IReadOnlyList<BankFeedLine> Lines, string? Cursor, bool HasMore, decimal? Balance = null);

/// <summary>A page, or the reason the call failed.</summary>
public sealed class BankFeedResult
{
    private BankFeedResult(BankFeedPage? page, BankFeedFailure? failure)
    {
        Page = page;
        Failure = failure;
    }

    public BankFeedPage? Page { get; }

    public BankFeedFailure? Failure { get; }

    public static BankFeedResult Succeeded(BankFeedPage page) => new(page, null);

    public static BankFeedResult Failed(BankFeedFailure failure) => new(null, failure);
}
