using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>
/// The corporate action feed port (docs/features/corporate-actions.md R7; ADR 0021 rule 1): one page of the
/// announcements of one market data connection after a cursor. Infrastructure dispatches to the adapter of the
/// connection. A failure comes back as a result, never as an exception the caller must parse.
/// </summary>
public interface ICorporateActionFeed
{
    Task<CorporateActionFeedResult> GetAnnouncementsAsync(
        string connection,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>An announcement as the party sent it. A field the party left out or sent in a shape the adapter cannot
/// read is null, and the pull refuses the page (R9), except <paramref name="Kind"/>: an unknown kind is null and is
/// stored.</summary>
public sealed record CorporateActionFeedItem(
    string? Id,
    string? ActionId,
    int? Version,
    PartyActionStatus? Status,
    string? Symbol,
    string? Currency,
    CorporateActionKind? Kind,
    int? RatioNew,
    int? RatioHeld,
    DateOnly? ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate);

/// <summary>One page: its items, oldest first, the party's new cursor and whether more items follow.</summary>
public sealed record CorporateActionFeedPage(IReadOnlyList<CorporateActionFeedItem> Items, string? Cursor, bool HasMore);

/// <summary>A page, or the reason the call failed.</summary>
public sealed class CorporateActionFeedResult
{
    private CorporateActionFeedResult(CorporateActionFeedPage? page, MarketDataFailure? failure)
    {
        Page = page;
        Failure = failure;
    }

    public CorporateActionFeedPage? Page { get; }

    public MarketDataFailure? Failure { get; }

    public static CorporateActionFeedResult Succeeded(CorporateActionFeedPage page) => new(page, null);

    public static CorporateActionFeedResult Failed(MarketDataFailure failure) => new(null, failure);
}
