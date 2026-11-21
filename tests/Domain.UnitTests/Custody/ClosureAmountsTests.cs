using MyWealthV2.Domain.Custody;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Custody;

/// <summary>Account closure R7: the payout is the cash balance rounded down to the minor unit; the remainder is the rest.</summary>
public class ClosureAmountsTests
{
    [TestCase("300.004", 2, "300.00", "0.004")]
    [TestCase("300.00", 2, "300.00", "0")]
    [TestCase("0.004", 2, "0", "0.004")]
    [TestCase("0", 2, "0", "0")]
    [TestCase("1200.4", 0, "1200", "0.4")]
    [TestCase("12.3456", 3, "12.345", "0.0006")]
    public void Split_GivesThePayoutRoundedDown_AndTheRemainder(
        string cashSum, int decimalPlaces, string payout, string remainder)
    {
        var (actualPayout, actualRemainder) = ClosureAmounts.Split(decimal.Parse(cashSum), decimalPlaces);

        actualPayout.ShouldBe(decimal.Parse(payout));
        actualRemainder.ShouldBe(decimal.Parse(remainder));
    }

    [Test]
    public void Split_RefusesANegativeCashSum() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ClosureAmounts.Split(-0.01m, 2));

    [TestCase("0", 2, true)]
    [TestCase("0.004", 2, true)]
    [TestCase("0.0099", 2, true)]
    [TestCase("0.01", 2, false)]
    [TestCase("-0.001", 2, false)]
    [TestCase("0.4", 0, true)]
    [TestCase("1", 0, false)]
    public void IsRemainder_IsTrueFrom0ToBelowTheMinorUnit(string cashSum, int decimalPlaces, bool expected) =>
        ClosureAmounts.IsRemainder(decimal.Parse(cashSum), decimalPlaces).ShouldBe(expected);

    [TestCase(2, "0.01")]
    [TestCase(0, "1")]
    [TestCase(3, "0.001")]
    public void MinorUnit_IsTenToTheMinusDecimalPlaces(int decimalPlaces, string unit) =>
        ClosureAmounts.MinorUnit(decimalPlaces).ShouldBe(decimal.Parse(unit));
}
