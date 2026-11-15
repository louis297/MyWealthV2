using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>
/// The cut-off date for a tenant and quote currency (trade-fees R9): the later of today (UTC) and the latest UTC date
/// of <c>bookedAt</c> of any <c>Buy</c> / <c>Sell</c> on an instrument quoted in that currency. Reversals do not count.
/// </summary>
internal static class TradeFeeCutOff
{
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public static async Task<DateOnly> ForAsync(
        IApplicationDbContext db,
        int tenantId,
        string quoteCurrency,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var byCurrency = await ForAsync(db, tenantId, [quoteCurrency], today, cancellationToken);
        return byCurrency[quoteCurrency];
    }

    public static async Task<Dictionary<string, DateOnly>> ForAsync(
        IApplicationDbContext db,
        int tenantId,
        IReadOnlyCollection<string> quoteCurrencies,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var codes = quoteCurrencies.Distinct(StringComparer.Ordinal).ToList();
        var latest = await (
                from leg in db.TransactionSecurityLegs
                join transaction in db.Transactions on leg.TransactionId equals transaction.Id
                join instrument in db.Instruments on leg.InstrumentId equals instrument.Id
                where transaction.TenantId == tenantId
                      && (transaction.Type == TransactionType.Buy || transaction.Type == TransactionType.Sell)
                      && codes.Contains(instrument.QuoteCurrency)
                group transaction.BookedAt by instrument.QuoteCurrency
                into trades
                select new { Currency = trades.Key, Latest = trades.Max() })
            .ToListAsync(cancellationToken);

        var result = codes.ToDictionary(code => code, _ => today, StringComparer.Ordinal);
        foreach (var row in latest)
        {
            var code = row.Currency.Trim().ToUpperInvariant();
            var tradeDate = DateOnly.FromDateTime(row.Latest.UtcDateTime);
            if (result.TryGetValue(code, out var current) && tradeDate > current)
            {
                result[code] = tradeDate;
            }
        }

        return result;
    }
}
