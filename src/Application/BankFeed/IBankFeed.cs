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
/// null; the pull refuses such a page (R10).</summary>
public sealed record BankFeedLine(
    string? Id,
    DateOnly? ValueDate,
    BankLineDirection? Direction,
    decimal? Amount,
    string? Currency,
    string? PayerName,
    string? PayerAccount,
    string? Reference);

/// <summary>One page: its lines, oldest first, the party's new cursor, and whether more lines follow.</summary>
public sealed record BankFeedPage(IReadOnlyList<BankFeedLine> Lines, string? Cursor, bool HasMore);

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
