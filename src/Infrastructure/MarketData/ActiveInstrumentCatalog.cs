using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class ActiveInstrumentCatalog(ApplicationDbContext db) : IActiveInstrumentCatalog
{
    public async Task<IReadOnlyList<ActiveInstrument>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Instruments.IgnoreQueryFilters()
            .Where(instrument => instrument.IsActive)
            .Select(instrument => new ActiveInstrument(instrument.Id, instrument.Symbol, instrument.QuoteCurrency))
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new ActiveInstrument(row.Id, row.Symbol.Trim(), row.QuoteCurrency.Trim()))
            .ToList();
    }
}
