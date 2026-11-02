using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Fx;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FxRateStore(ApplicationDbContext db, TimeProvider time) : IFxRate, IFxRateStore
{
    public async Task<decimal> GetRateAsync(
        string from,
        string to,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var source = from.Trim().ToUpperInvariant();
        var target = to.Trim().ToUpperInvariant();
        var date = asOf is null
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : DateOnly.FromDateTime(asOf.Value.DateTime);

        var rows = await db.FxRates.AsNoTracking()
            .Where(row => row.BaseCurrency == source && row.QuoteCurrency == target)
            .Select(row => new FxRateQuote(row.BaseCurrency, row.QuoteCurrency, row.Rate, row.AsOfDate))
            .ToListAsync(cancellationToken);

        return FxRateSelector.Select(source, target, date, rows);
    }

    public Task UpsertAsync(
        IReadOnlyList<FxRateQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        _ = quotes;
        _ = fetchedAt;
        _ = cancellationToken;
        throw new NotImplementedException();
    }
}
