using MyWealthV2.Application.MarketData;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class InstrumentPriceStore(ApplicationDbContext db, TimeProvider time) : IInstrumentPriceStore
{
    public Task UpsertAsync(
        IReadOnlyList<InstrumentPriceQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        _ = db;
        _ = time;
        _ = quotes;
        _ = fetchedAt;
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
