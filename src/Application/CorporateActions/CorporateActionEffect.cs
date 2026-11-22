using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Corporate-actions R22: a corporate action takes effect. Hand entry (R20), the apply step (R23, R24) and
/// the feed processing (R12) all make an action effective here.</summary>
internal static class CorporateActionEffect
{
    /// <summary>
    /// The action becomes <c>Effective</c> at <paramref name="at"/> and one <c>Pending</c> entitlement is stored for
    /// every Custodial account of its Tenant that holds the instrument now (a holding with quantity above 0). External
    /// accounts get none, and the set never grows. The open orders of the Tenant in the instrument are asked to be
    /// cancelled (orders R50). The caller runs this in one database transaction with the save.
    /// </summary>
    public static async Task MakeEffectiveAsync(
        IApplicationDbContext db,
        CorporateAction action,
        DateTimeOffset at,
        int? byUserId,
        CancellationToken cancellationToken)
    {
        action.MakeEffective(at, byUserId);
        var accountIds = await (
                from holding in db.Holdings.IgnoreQueryFilters()
                join account in db.Accounts.IgnoreQueryFilters() on holding.AccountId equals account.Id
                where holding.TenantId == action.TenantId
                      && holding.InstrumentId == action.InstrumentId
                      && holding.Quantity > 0
                      && account.CustodyMode == CustodyMode.Custodial
                select account.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        foreach (var accountId in accountIds)
        {
            db.CorporateActionEntitlements.Add(CorporateActionEntitlement.Create(action.TenantId, action.Id, accountId));
        }

        // Orders R50: an order's quantity and price limit are on the old share basis, so every open order of the
        // Tenant in the Instrument that has no cancel request gets one from the platform. One the broker cannot hold
        // yet ends at once.
        var open = Enum.GetValues<OrderStatus>().Where(Order.IsOpenStatus).ToArray();
        var orders = await db.Orders.IgnoreQueryFilters()
            .Where(order => order.TenantId == action.TenantId
                            && order.InstrumentId == action.InstrumentId
                            && open.Contains(order.Status)
                            && order.CancelRequestedAt == null)
            .OrderBy(order => order.Id)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            if (order.IsUnsent)
            {
                order.CancelUnsent(null, Order.CorporateActionNote, at);
            }
            else
            {
                order.RequestCancel(null, Order.CorporateActionNote, at);
            }
        }
    }
}
