using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class ActiveInstrumentCatalog(ApplicationDbContext db) : IActiveInstrumentCatalog
{
    public Task<IReadOnlyList<ActiveInstrument>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        _ = db;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<ActiveInstrument>>([]);
    }
}
