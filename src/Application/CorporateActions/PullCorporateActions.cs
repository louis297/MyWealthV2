using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>What a pull of one connection did: the failure that stopped it, if any, and how many announcements it
/// stored.</summary>
public sealed record CorporateActionPullOutcome(MarketDataFailure? Failure, int Stored)
{
    public bool Failed => Failure is not null;
}

/// <summary>
/// Reads the corporate action announcements of one market data connection through <see cref="ICorporateActionFeed"/>
/// after the feed's cursor and stores each new one once, with the new cursor, page by page (corporate-actions R8 step 1,
/// R9, R10; ADR 0021 rules 6, 7, 15, 16). Platform data: no Tenant.
/// </summary>
public sealed class PullCorporateActions
{
    public const int PageLimit = 100;
    public const int MaxPages = 20;

    public Task<CorporateActionPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
