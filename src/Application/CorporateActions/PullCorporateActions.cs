using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.MarketData;

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
public sealed class PullCorporateActions(
    IApplicationDbContext db,
    ICorporateActionFeed feed,
    MarketDataConnections connections,
    TimeProvider time,
    ILogger<PullCorporateActions> logger)
{
    public const int PageLimit = 100;
    public const int MaxPages = 20;

    public async Task<CorporateActionPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (connections.Named(connection) is null)
        {
            logger.LogWarning("Market data connection {Connection} is not configured on this host; nothing is read.", connection);
            return new CorporateActionPullOutcome(null, 0);
        }

        var stored = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            db.ClearTrackedChanges();
            var state = await FeedAsync(connection, cancellationToken);
            var result = await feed.GetAnnouncementsAsync(connection, state.Cursor, PageLimit, cancellationToken);
            var failure = result.Failure ?? Check(result.Page!);
            if (failure is not null)
            {
                // R8: nothing more is stored and the cursor stays.
                state.Failed(failure.Value, time.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                logger.LogError("Corporate action read failed: connection {Connection}, failure {Failure}.", connection, failure);
                return new CorporateActionPullOutcome(failure, stored);
            }

            stored += await StorePageAsync(connection, state, result.Page!, cancellationToken);
            if (!result.Page!.HasMore)
            {
                break;
            }
        }

        return new CorporateActionPullOutcome(null, stored);
    }

    /// <summary>R9: a page with any item the platform cannot store is refused whole. A cursor that does not fit is a
    /// response the platform cannot read.</summary>
    private static MarketDataFailure? Check(CorporateActionFeedPage page)
    {
        if (page.Cursor is { Length: > MarketDataFeed.CursorMaxLength })
        {
            return MarketDataFailure.BadResponse;
        }

        return page.Items.All(item => CorporateActionAnnouncement.IsStorable(
            item.Id, item.ActionId, item.Version, item.Status, item.Symbol, item.Currency, item.RatioNew, item.RatioHeld, item.ExDate))
            ? null
            : MarketDataFailure.InvalidItem;
    }

    /// <summary>R9, R10: the new announcements of one page, the new cursor and the success time, in one database
    /// transaction. An announcement already stored for the connection, or earlier in the page, is skipped.</summary>
    private async Task<int> StorePageAsync(
        string connection,
        MarketDataFeed state,
        CorporateActionFeedPage page,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var added = 0;
        await db.ExecuteInTransactionAsync(async token =>
        {
            added = 0;
            var ids = page.Items.Select(item => item.Id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var known = await db.CorporateActionAnnouncements
                .Where(row => row.Connection == connection && ids.Contains(row.OutsideId))
                .Select(row => row.OutsideId)
                .ToListAsync(token);

            // The database compares outside ids without regard to case, so the page does too.
            var seen = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
            foreach (var item in page.Items)
            {
                if (!seen.Add(item.Id!))
                {
                    continue;
                }

                db.CorporateActionAnnouncements.Add(CorporateActionAnnouncement.Receive(
                    connection, item.Id!, item.ActionId!, item.Version!.Value, item.Status!.Value, item.Symbol!, item.Currency!,
                    item.Kind, item.RatioNew!.Value, item.RatioHeld!.Value, item.ExDate!.Value, item.RecordDate, item.PaymentDate, now));
                added++;
            }

            state.Advance(page.Cursor, now);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        return added;
    }

    private async Task<MarketDataFeed> FeedAsync(string connection, CancellationToken cancellationToken)
    {
        var state = await db.MarketDataFeeds
            .SingleOrDefaultAsync(row => row.Connection == connection && row.Kind == MarketDataFeedKind.CorporateActions, cancellationToken);
        if (state is null)
        {
            state = MarketDataFeed.Start(connection, MarketDataFeedKind.CorporateActions);
            db.MarketDataFeeds.Add(state);
        }

        return state;
    }
}
