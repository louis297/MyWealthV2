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

    [Test]
    public void Select_UsesTheLatestRowOnOrBeforeTheRequestedDate()
    {
        var rows = new FxRateQuote[]
        {
            new("usd", "NZD", 1.50000000m, new DateOnly(2026, 1, 1)),
            new("USD", "nzd", 1.60000000m, new DateOnly(2026, 10, 11)),
            new("EUR", "JPY", 160m, new DateOnly(2026, 10, 11))
        };

        FxRateSelector.Select("USD", "NZD", new DateOnly(2026, 10, 11), rows).ShouldBe(1.60000000m);
        FxRateSelector.Select("USD", "NZD", new DateOnly(2026, 10, 10), rows).ShouldBe(1.50000000m);
        FxRateSelector.Select("USD", "NZD", Today, rows).ShouldBe(1.60000000m);

        var missing = Should.Throw<FxRateNotFoundException>(() =>
            FxRateSelector.Select("USD", "NZD", new DateOnly(2025, 12, 31), rows));
        missing.BaseCurrency.ShouldBe("USD");
        missing.QuoteCurrency.ShouldBe("NZD");
        missing.AsOf.ShouldBe(new DateOnly(2025, 12, 31));
    }
}
