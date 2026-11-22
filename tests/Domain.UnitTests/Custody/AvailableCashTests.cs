using MyWealthV2.Domain.Custody;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Custody;

/// <summary>Withdrawals R35–R37: available cash.</summary>
public class AvailableCashTests
{
    [TestCase(1000, 300, 700)]
    [TestCase(1000, 0, 1000)]
    [TestCase(300, 300, 0)]
    public void Of_IsTheCashSumMinusReserved(decimal sum, decimal reserved, decimal available) =>
        AvailableCash.Of(sum, reserved).ShouldBe(available);

    [TestCase(700, 300, true)]
    [TestCase(300, 300, true)]
    [TestCase(299.99, 300, false)]
    [TestCase(0, 0, true)]
    [TestCase(-0.01, 0, false)]
    public void Allows_WhenTheSumAfterTheWriteCoversTheReservation(decimal after, decimal reserved, bool allowed) =>
        AvailableCash.Allows(after, reserved).ShouldBe(allowed);

    // Orders R44.
    [TestCase(6200, 1055.25, 2000, 3144.75)]
    [TestCase(1000, 0, 0, 1000)]
    [TestCase(1000, 300, 700, 0)]
    [TestCase(4985, 4221, 985, 0)]
    [TestCase(100, 0, 500, 0)]
    public void Withdrawable_IsAvailableCashMinusUnsettledCash_AndNeverBelowZero(
        decimal sum, decimal reserved, decimal unsettled, decimal withdrawable) =>
        AvailableCash.Withdrawable(sum, reserved, unsettled).ShouldBe(withdrawable);
}
