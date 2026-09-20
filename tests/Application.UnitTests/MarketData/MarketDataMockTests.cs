using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

public class MarketDataMockTests
{
    [Test]
    public void TryGetPrice_Missing_IsEmpty()
    {
        var prices = new MarketDataMock();

        prices.TryGetPrice(42).ShouldBeNull();
    }
}
