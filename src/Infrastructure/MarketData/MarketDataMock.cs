using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class MarketDataMock : IMarketData
{
    public Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null) =>
        Money.Create(0, "USD");
}
