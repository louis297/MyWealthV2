using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>The stored figures of one dividend: its quote currency, the decimal places of it, its entitlements and its
/// expected amount (dividends R15, R25).</summary>
internal sealed record DividendFigures(string Currency, int DecimalPlaces, int Entitlements, decimal ExpectedAmount)
{
    public static async Task<DividendFigures> OfAsync(
        IApplicationDbContext db,
        ICurrencyCatalog currencies,
        CorporateAction action,
        CancellationToken cancellationToken)
    {
        var currency = (await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == action.InstrumentId)
            .Select(row => row.QuoteCurrency)
            .SingleAsync(cancellationToken)).Trim();
        var places = currencies.TryGet(currency)?.DecimalPlaces
                     ?? throw new InvalidOperationException($"Currency {currency} is not in the catalog.");
        var entitlements = await db.CorporateActionEntitlements.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CorporateActionId == action.Id)
            .ToListAsync(cancellationToken);
        return new DividendFigures(
            currency, places, entitlements.Count, DividendPayment.ExpectedAmount(entitlements, action.ReinvestedByCustodian(places)));
    }
}
