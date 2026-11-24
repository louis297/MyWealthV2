using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The custodian feed port (docs/features/in-kind-transfers.md R8, R73; ADR 0021 rules 1, 5–8): one page of a
/// safekeeping account's custody movements after a cursor, with the custodian's positions at that page's cursor. A
/// failure comes back as a result, never as an exception the caller must parse.
/// </summary>
public interface ICustodianFeed
{
    Task<CustodianFeedResult> GetMovementsAsync(CustodianFeedRequest request, CancellationToken cancellationToken);
}

/// <summary>Movements of <paramref name="AccountNumber"/> after <paramref name="Cursor"/> (none: from the start).</summary>
public sealed record CustodianFeedRequest(string Connection, string AccountNumber, string? Cursor, int Limit);

/// <summary>A movement as the custodian sent it. A field the adapter could not read is null, and the pull refuses
/// such a page (R11), except <paramref name="Kind"/>: a kind the adapter does not know is null and is stored so.</summary>
public sealed record CustodianFeedMovement(
    string? Id,
    CustodyMovementDirection? Direction,
    CustodyMovementKind? Kind,
    string? Symbol,
    string? Currency,
    decimal? Quantity,
    DateOnly? SettlementDate,
    string? InstructionId = null,
    string? TradeReference = null,
    string? CorporateActionId = null,
    string? CounterpartyName = null,
    string? CounterpartyAccount = null,
    string? Reference = null);

/// <summary>R12: the custodian's quantity of one symbol at the page's cursor.</summary>
public sealed record CustodianFeedPosition(string? Symbol, string? Currency, decimal? Quantity);

/// <summary>One page: its movements, oldest first, the party's new cursor, whether more follow, and the positions at
/// the cursor (null when the party sent none, which the pull refuses).</summary>
public sealed record CustodianFeedPage(
    IReadOnlyList<CustodianFeedMovement> Movements,
    string? Cursor,
    bool HasMore,
    IReadOnlyList<CustodianFeedPosition>? Positions);

/// <summary>A page, or the reason the call failed.</summary>
public sealed class CustodianFeedResult
{
    private CustodianFeedResult(CustodianFeedPage? page, CustodianFailure? failure)
    {
        Page = page;
        Failure = failure;
    }

    public CustodianFeedPage? Page { get; }

    public CustodianFailure? Failure { get; }

    public static CustodianFeedResult Succeeded(CustodianFeedPage page) => new(page, null);

    public static CustodianFeedResult Failed(CustodianFailure failure) => new(null, failure);
}
