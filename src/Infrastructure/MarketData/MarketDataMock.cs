using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class MarketDataMock : IMarketData
{
    private readonly Dictionary<int, Money> _prices = [];
    private readonly object _gate = new();

    public void SetPrice(int instrumentId, Money price)
    {
        ArgumentNullException.ThrowIfNull(price);
        lock (_gate)
        {
            _prices[instrumentId] = price;
        }
    }

    public Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null)
    {
        lock (_gate)
        {
            return _prices.TryGetValue(instrumentId, out var price) ? price : null;
        }
    }
}
