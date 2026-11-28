using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>FIF income R7: the fair dividend rate of a tax year.</summary>
public class FairDividendRateTests
{
    [TestCase(2009)]
    [TestCase(2026)]
    [TestCase(2027)]
    public void For_Is5Percent_From2009On(int taxYear)
    {
        FairDividendRate.For(taxYear).ShouldBe(0.05m);
    }

    [Test]
    public void For_ATaxYearBefore2009_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FairDividendRate.For(2008));
    }
}
