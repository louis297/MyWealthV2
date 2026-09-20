using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IMarketData
{
    Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null);
}
