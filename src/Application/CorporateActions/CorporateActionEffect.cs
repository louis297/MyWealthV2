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
                select new
                {
                    AccountId = account.Id,
                    holding.Quantity,
                    account.ReinvestDividends,
                    AccountCurrency = account.Currency,
                    account.CustomerId,
                    Assigned = db.AccountPortfolioAssignments.IgnoreQueryFilters()
                        .Any(row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active),
                    Residency = db.CustomerTaxProfiles.IgnoreQueryFilters()
                        .Where(row => row.UserId == account.CustomerId)
                        .Select(row => (TaxResidency?)row.TaxResidency)
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);

        // Dividends R18: with a plan, an entitlement reinvests only through the Tenant's safekeeping account in the
        // quote currency.
        var safekeeping = terms.ReinvestmentPrice is null
            ? null
            : await Custodian.OrderSafekeeping.ActiveAsync(db, action.TenantId, quoteCurrency, cancellationToken);
        var pending = 0;
        var electionQuantity = 0m;
        foreach (var holding in holdings)
        {
            var amounts = terms.SourceAmounts(holding.Quantity, places);
            decimal? participating = null;
            if (safekeeping is not null
                && holding.ReinvestDividends
                && string.Equals(holding.AccountCurrency.Trim(), quoteCurrency, StringComparison.Ordinal)
                && !holding.Assigned
                && amounts.Gross > 0m)
            {
                // R19: the New Zealand tax of the Customer's residency now stays in cash.
                var tax = DividendWithholding.For(holding.Residency ?? CustomerTaxProfile.NotProvided.TaxResidency, terms.TaxSource);
                var part = DividendWithholding.ParticipatingQuantity(holding.Quantity, terms, tax);
                participating = part > 0m ? part : null;
            }

            var entitlement = CorporateActionEntitlement.CreateDividend(
                action.TenantId, action.Id, holding.AccountId, holding.Quantity, amounts, participating is not null, participating, at);
            db.CorporateActionEntitlements.Add(entitlement);
            electionQuantity += participating ?? 0m;
            if (entitlement.Status == EntitlementStatus.Pending)
            {
                pending++;
            }
        }

        // R20: the election of the dividend, in the same transaction.
        action.SetElection(electionQuantity > 0m ? electionQuantity : null, electionQuantity > 0m ? safekeeping?.Id : null);

        if (pending == 0)
        {
            action.MarkApplied(at);
        }
    }
}
