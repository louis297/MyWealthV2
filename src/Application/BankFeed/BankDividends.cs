using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Dividends R24, R25: a credit transfer with no <c>returnOf</c> whose reference text, trimmed and without regard to
/// case, is the party action id of a <c>Feed</c> dividend of the line's Tenant quoted in the line's currency is that
/// dividend's receipt and never a deposit. It is matched when the dividend waits for its payment and the amount is
/// within the rounding allowance of the expected amount; otherwise it goes to suspense as <c>DividendMismatch</c>.
/// </summary>
internal sealed class BankDividends(IApplicationDbContext db, ICurrencyCatalog currencies, TimeProvider time)
{
    /// <summary>True when the line was a dividend receipt.</summary>
    public async Task<bool> ProcessReceiptAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        var reference = line.ReferenceText?.Trim();
        if (line.Direction != BankLineDirection.Credit
            || line.Kind != StatementLineKind.Transfer
            || line.ReturnOfOutsideId is not null
            || string.IsNullOrEmpty(reference))
        {
            return false;
        }

        var key = reference.ToUpperInvariant();
        var currency = line.Currency.Trim();
        var action = await (
                from row in db.CorporateActions.IgnoreQueryFilters()
                join instrument in db.Instruments.IgnoreQueryFilters() on row.InstrumentId equals instrument.Id
                where row.TenantId == line.TenantId
                      && row.Origin == CorporateActionOrigin.Feed
                      && row.Kind == CorporateActionKind.Dividend
                      && row.PartyActionId!.ToUpper() == key
                      && instrument.QuoteCurrency == currency
                orderby row.Id
                select row)
            .FirstOrDefaultAsync(cancellationToken);
        if (action is null)
        {
            return false;
        }

        var figures = await DividendFigures.OfAsync(db, currencies, action, cancellationToken);
        if (action.Status != CorporateActionStatus.Effective
            || action.ReceiptLineId is not null
            || !DividendPayment.Fits(line.Amount, figures.ExpectedAmount, figures.Entitlements, figures.DecimalPlaces))
        {
            line.MarkDividendMismatch();
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        line.MarkDividendReceipt(action.Id, time.GetUtcNow());
        action.TakeReceipt(line.Id, line.Amount);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
