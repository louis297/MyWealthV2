using MyWealthV2.Domain.Tax;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Tax;

/// <summary>Tax-cost-threshold R15: the threshold of a tax year.</summary>
public class FifThresholdTests
{
    [TestCase(2009)]
    [TestCase(2025)]
    [TestCase(2026)]
    public void For_IsNz50000_From2009To2026(int taxYear)
    {
        FifThreshold.For(taxYear).ShouldBe(50_000.00m);
    }

    [TestCase(2008)]
    [TestCase(2027)]
    [TestCase(2030)]
    public void For_HasNoValue_Before2009AndFrom2027(int taxYear)
    {
        FifThreshold.For(taxYear).ShouldBeNull();
    }
}
