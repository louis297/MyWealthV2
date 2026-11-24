using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.CorporateActions;
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
        ICurrencyCatalog currencies,
        CorporateAction action,
        DateTimeOffset at,
        int? byUserId,
        CancellationToken cancellationToken)
    {
        action.MakeEffective(at, byUserId);
        if (action.Terms is { } terms)
        {
            await GoExAsync(db, currencies, action, terms, at, cancellationToken);
            return;
        }

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

    /// <summary>
    /// Dividends R6, R13, R14, R16: a dividend goes ex. One entitlement per Custodial account of the Tenant that holds
    /// the instrument now, with the holding's whole quantity as its entitled quantity and the source amounts in the
    /// quote currency; nothing is posted and no order is cancelled. A dividend with no entitlement left
    /// <c>Pending</c> is <c>Applied</c> at once and expects no payment.
    /// </summary>
    private static async Task GoExAsync(
        IApplicationDbContext db,
        ICurrencyCatalog currencies,
        CorporateAction action,
        DividendTerms terms,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var quoteCurrency = (await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == action.InstrumentId)
            .Select(row => row.QuoteCurrency)
            .SingleAsync(cancellationToken)).Trim();
        var places = currencies.TryGet(quoteCurrency)?.DecimalPlaces
                     ?? throw new InvalidOperationException($"Currency {quoteCurrency} is not in the catalog.");
        var holdings = await (
                from holding in db.Holdings.IgnoreQueryFilters()
                join account in db.Accounts.IgnoreQueryFilters() on holding.AccountId equals account.Id
                where holding.TenantId == action.TenantId
                      && holding.InstrumentId == action.InstrumentId
                      && holding.Quantity > 0
                      && account.CustodyMode == CustodyMode.Custodial
                orderby account.Id
                select new { AccountId = account.Id, holding.Quantity })
            .ToListAsync(cancellationToken);

        var pending = 0;
        foreach (var holding in holdings)
        {
            var entitlement = CorporateActionEntitlement.CreateDividend(
                action.TenantId, action.Id, holding.AccountId, holding.Quantity, terms.SourceAmounts(holding.Quantity, places),
                false, null, at);
            db.CorporateActionEntitlements.Add(entitlement);
            if (entitlement.Status == EntitlementStatus.Pending)
            {
                pending++;
            }
        }

        if (pending == 0)
        {
            action.MarkApplied(at);
        }
    }
}
