using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FxRateStore(ApplicationDbContext db) : IFxRate
{
    public decimal GetRate(string from, string to, DateTimeOffset? asOf = null)
    {
        _ = db;
        throw new NotImplementedException();
    }
}
