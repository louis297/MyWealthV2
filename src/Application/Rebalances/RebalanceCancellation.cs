using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Rebalances;

/// <summary>
/// Rebalances R19, R22: cancels an open rebalance. A <c>Proposed</c> one is <c>Cancelled</c> at once. One in progress
/// gets a cancel request, and each of its open orders is cancelled as orders R18 does: at once before its first send,
/// otherwise a request the broker answers. It becomes <c>Cancelled</c> when none of its orders is live, here when that
/// is already so, otherwise in the advance step. The caller holds the Account lock and commits.
/// </summary>
internal static class RebalanceCancellation
{
    /// <summary><paramref name="byUserId"/> is null for the platform's cancel (an assignment that ended).</summary>
    public static async Task CancelAsync(
        IApplicationDbContext db,
        Rebalance rebalance,
        int? byUserId,
        string? note,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reason = byUserId is null ? RebalanceCancelReason.Unassigned : RebalanceCancelReason.Requested;
        if (rebalance.Status == RebalanceStatus.Proposed)
        {
            rebalance.Cancel(reason, now, anyOrderLive: false, byUserId, note);
            return;
        }

        rebalance.RequestCancel(byUserId, note, now);
        var orders = await db.Orders.IgnoreQueryFilters().Include(row => row.Fills)
            .Where(row => row.RebalanceId == rebalance.Id)
            .ToListAsync(cancellationToken);
        foreach (var order in orders.Where(order => order.IsOpen && !order.HasCancelRequest))
        {
            if (order.IsUnsent)
            {
                order.CancelUnsent(byUserId, Order.RebalanceCancelledNote, now, forRebalance: true);
            }
            else
            {
                order.RequestCancel(byUserId, Order.RebalanceCancelledNote, now, forRebalance: true);
            }
        }

        if (!orders.Any(order => order.IsLive))
        {
            rebalance.Cancel(reason, now, anyOrderLive: false);
        }
    }
}
