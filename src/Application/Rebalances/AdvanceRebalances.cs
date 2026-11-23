using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalances;

namespace MyWealthV2.Application.Rebalances;

/// <summary>What the advance step did: rebalances that ended or placed a wave, and rebalances it failed on.</summary>
public sealed record RebalanceAdvanceOutcome(int Advanced, int Failed);

/// <summary>
/// Rebalances R17: the sixth step of <c>broker-sync:{connection}</c>, after the fills are posted. Every rebalance in
/// progress that took the connection and has no live order ends, or places its next wave. It reads past the tenant
/// query filter and takes the Tenant from each row, as <see cref="ProcessExecutionReports"/>.
/// </summary>
public sealed class AdvanceRebalances(
    IApplicationDbContext db,
    IBrokerConnections connections,
    ProposeRebalance proposals,
    RebalancePlanning planning,
    PlaceRebalanceWave waves,
    TimeProvider time,
    ILogger<AdvanceRebalances> logger)
{
    public const string NotFilledCode = "order_not_filled";

    public async Task<RebalanceAdvanceOutcome> RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Broker connection {Connection} is not configured on this host; no rebalance is advanced.", connection);
            return new RebalanceAdvanceOutcome(0, 0);
        }

        db.ClearTrackedChanges();
        var inProgress = await db.Rebalances.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Connection == connection
                && (row.Status == RebalanceStatus.Selling || row.Status == RebalanceStatus.Buying))
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.PublicId, row.AccountId })
            .ToListAsync(cancellationToken);

        var (advanced, failed) = (0, 0);
        foreach (var item in inProgress)
        {
            db.ClearTrackedChanges();
            try
            {
                var moved = false;
                await db.ExecuteInTransactionAsync(async token =>
                {
                    await db.LockAccountAsync(item.AccountId, token);
                    moved = await AdvanceAsync(item.Id, token);
                }, cancellationToken);
                if (moved)
                {
                    advanced++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                db.ClearTrackedChanges();
                failed++;
                logger.LogError(exception, "Rebalance {RebalanceId} could not be advanced; the next run tries again.", item.PublicId);
            }
        }

        return new RebalanceAdvanceOutcome(advanced, failed);
    }

    /// <summary>R17 (a)–(f), under the Account lock and guarded by the rebalance's <c>RowVersion</c>. False when the
    /// rebalance is no longer in progress or still has a live order.</summary>
    private async Task<bool> AdvanceAsync(int rebalanceId, CancellationToken cancellationToken)
    {
        var rebalance = await db.Rebalances.IgnoreQueryFilters().Include(row => row.Targets)
            .SingleAsync(row => row.Id == rebalanceId, cancellationToken);
        if (!rebalance.IsInProgress)
        {
            return false;
        }

        var orders = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills)
            .Where(row => row.RebalanceId == rebalance.Id)
            .ToListAsync(cancellationToken);
        if (orders.Any(order => order.IsLive))
        {
            return false;
        }

        var now = time.GetUtcNow();
        await EndOrPlaceAsync(rebalance, orders, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        // R12: a weight change during the rebalance is proposed when it ends.
        await proposals.FollowUpAsync(rebalance, cancellationToken);
        return true;
    }

    private async Task EndOrPlaceAsync(Rebalance rebalance, IReadOnlyList<Order> orders, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // (a) a cancel request: the person's, or the platform's when its assignment ended (R22).
        if (rebalance.HasCancelRequest)
        {
            rebalance.Cancel(
                rebalance.CancelRequestedByUserId is null ? RebalanceCancelReason.Unassigned : RebalanceCancelReason.Requested,
                now,
                anyOrderLive: false);
            return;
        }

        // (b) an order of the last wave was not filled in full (R21).
        var lastWave = orders.Count == 0 ? [] : orders.Where(order => order.PlacedAt == orders.Max(row => row.PlacedAt)).ToList();
        var notFilled = lastWave.Count(order => order.Status != OrderStatus.Filled);
        if (notFilled > 0)
        {
            rebalance.EndIncomplete(NotFilledCode, $"{notFilled} of {lastWave.Count} orders were not filled in full.", now);
            return;
        }

        // (c) the buys and one top-up are placed.
        if (rebalance.BuyWaves >= RebalanceRules.MaxBuyWaves)
        {
            rebalance.Complete(now);
            return;
        }

        // (d) R6, then the plan against the rebalance's own targets, sized by reservation (R16).
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.Id == rebalance.AccountId, cancellationToken);
        var portfolio = await db.Portfolios.IgnoreQueryFilters().SingleAsync(row => row.Id == rebalance.PortfolioId, cancellationToken);
        if (await proposals.EligibilityProblemAsync(account, portfolio, cancellationToken) is { } problem)
        {
            rebalance.EndIncomplete(problem.Code, problem.Message, now);
            return;
        }

        var planned = await planning.PlanAsync(account, RebalanceTargets.Of(rebalance), sizeBuysByReservation: true, now, cancellationToken);
        if (planned.Failed)
        {
            rebalance.EndIncomplete(planned.Plan.Code!, planned.Plan.Message!, now);
            return;
        }

        // (e) nothing to buy; (f) otherwise the buys are the next wave, sent by the next run.
        try
        {
            if ((await waves.PlaceAsync(rebalance, account, planned, now, cancellationToken)).Count == 0)
            {
                rebalance.Complete(now);
            }
        }
        catch (RebalanceException exception)
        {
            rebalance.EndIncomplete(exception.Code, exception.Detail ?? exception.Message, now);
        }
    }
}
