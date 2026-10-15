using MyWealthV2.Application.Reports;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Reports;

public class CashAmountTextTests
{
    [Test]
    public void Format_UsesTheCurrencyDecimalPlaces()
    {
        CashAmountText.Format(1.5m, "NZD", 2).ShouldBe("1.50 NZD");
        CashAmountText.Format(100m, "JPY", 0).ShouldBe("100 JPY");
        CashAmountText.Format(-1.5m, "NZD", 2).ShouldBe("-1.50 NZD");
    }
}
