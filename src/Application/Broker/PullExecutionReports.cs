using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker;

/// <summary>What a pull of one connection did: the failure that stopped it, if any, and how many reports it stored.</summary>
public sealed record BrokerPullOutcome(BrokerFailure? Failure, int Stored)
{
    public bool Failed => Failure is not null;
}

/// <summary>
/// Reads the execution reports of one broker connection through <see cref="IBrokerFeed"/> after the feed's cursor and
/// stores each new one once, with the new cursor, page by page (docs/features/orders.md R24; ADR 0021 rules 6, 7, 15,
/// 16). Platform data: no Tenant.
/// </summary>
public sealed class PullExecutionReports(
    IApplicationDbContext db,
    IBrokerFeed feed,
    IBrokerConnections connections,
    TimeProvider time,
    ILogger<PullExecutionReports> logger)
{
    public const int PageLimit = 100;
    public const int MaxPages = 20;

    public async Task<BrokerPullOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; nothing is read.", connection);
            return new BrokerPullOutcome(null, 0);
        }

        var stored = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            db.ClearTrackedChanges();
            var state = await FeedAsync(connection, cancellationToken);
            var result = await feed.GetReportsAsync(connection, state.Cursor, PageLimit, cancellationToken);
            var failure = result.Failure ?? Check(result.Page!);
            if (failure is not null)
            {
                // R24: nothing of the page is stored and the cursor stays.
                state.RecordFailure(failure.Value, time.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                logger.LogError("Execution report read failed: connection {Connection}, failure {Failure}.", connection, failure);
                return new BrokerPullOutcome(failure, stored);
            }

            stored += await StorePageAsync(connection, state, result.Page!, cancellationToken);
            if (!result.Page!.HasMore)
            {
                break;
            }
        }

        return new BrokerPullOutcome(null, stored);
    }

    /// <summary>R24: a page with any item the platform cannot store is refused whole. A cursor that does not fit is a
    /// response the platform cannot read.</summary>
    private static BrokerFailure? Check(BrokerFeedPage page)
    {
        if (page.Cursor is { Length: > BrokerFeed.CursorMaxLength })
        {
            return BrokerFailure.BadResponse;
        }

        return page.Items.All(item => ExecutionReport.IsStorable(
            item.Id, item.Kind, item.ClientOrderId, item.Quantity, item.Price, item.TradeDate, item.SettlementDate, item.FillId,
            item.SettledDate))
            ? null
            : BrokerFailure.InvalidItem;
    }

    /// <summary>R24: the new reports of one page, the new cursor and the success time, in one database transaction. A
    /// report already stored for the connection, or earlier in the page, is skipped and not changed.</summary>
    private async Task<int> StorePageAsync(
        string connection,
        BrokerFeed state,
        BrokerFeedPage page,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var added = 0;
        await db.ExecuteInTransactionAsync(async token =>
        {
            added = 0;
            var ids = page.Items.Select(item => item.Id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var known = await db.ExecutionReports
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

                db.ExecutionReports.Add(ExecutionReport.Receive(
                    connection, item.Id!, item.Kind, item.ClientOrderId, item.BrokerOrderId, item.Quantity, item.Price,
                    item.TradeDate, item.SettlementDate, item.ContractNote, item.FillId, item.SettledDate, item.Reason, now));
                added++;
            }

            state.Advance(page.Cursor, now);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        return added;
    }

    private async Task<BrokerFeed> FeedAsync(string connection, CancellationToken cancellationToken)
    {
        var state = await db.BrokerFeeds.SingleOrDefaultAsync(row => row.Connection == connection, cancellationToken);
        if (state is null)
        {
            state = BrokerFeed.Start(connection);
            db.BrokerFeeds.Add(state);
        }

        return state;
    }
}
