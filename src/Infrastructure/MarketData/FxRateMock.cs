using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FxRateMock : IFxRate
{
    public decimal GetRate(string from, string to, DateTimeOffset? asOf = null) => 1m;
}
