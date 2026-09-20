using MyWealthV2.Domain.ValueObjects;
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

    [Test]
    public void TryGetPrice_Seeded_ReturnsQuoteCurrencyMoney()
    {
        var prices = new MarketDataMock();
        prices.SetPrice(7, Money.Create(123.45m, "USD"));

        var price = prices.TryGetPrice(7);
        price.ShouldNotBeNull();
        price.Amount.ShouldBe(123.45m);
        price.Currency.ShouldBe("USD");
    }
}
