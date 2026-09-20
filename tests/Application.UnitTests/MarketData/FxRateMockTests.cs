using MyWealthV2.Infrastructure.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.MarketData;

public class FxRateMockTests
{
    [Test]
    public void GetRate_SameCurrencyCi_Returns1()
    {
        var fx = new FxRateMock();

        fx.GetRate("nzd", "NZD").ShouldBe(1m);
        fx.GetRate("USD", "usd").ShouldBe(1m);
    }

    [Test]
    public void GetRate_MissingCrossPair_DoesNotReturn1()
    {
        var fx = new FxRateMock();

        Should.Throw<InvalidOperationException>(() => fx.GetRate("USD", "NZD"))
            .Message.ShouldNotBeNull();
    }

    [Test]
    public void GetRate_SeededCrossPair_ReturnsRate()
    {
        var fx = new FxRateMock();
        fx.SetRate("USD", "NZD", 1.6m);

        fx.GetRate("usd", "nzd").ShouldBe(1.6m);
    }
}
