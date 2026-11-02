using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Fx;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Fx;

public class FxRateSelectorTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Test]
    public void Select_SameCurrency_Returns1()
    {
        FxRateSelector.Select("nzd", "NZD", Today, []).ShouldBe(1m);
        FxRateSelector.Select("USD", "usd", Today, []).ShouldBe(1m);
    }

    [Test]
    public void Select_MissingPair_ThrowsWithTheRequestedDate()
    {
        var exception = Should.Throw<FxRateNotFoundException>(() =>
            FxRateSelector.Select("usd", "nzd", Today, []));

        exception.BaseCurrency.ShouldBe("USD");
        exception.QuoteCurrency.ShouldBe("NZD");
        exception.AsOf.ShouldBe(Today);
    }
}
