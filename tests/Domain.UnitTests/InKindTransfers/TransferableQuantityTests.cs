using MyWealthV2.Domain.Custody;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R26, R44: the transferable quantity of a holding.</summary>
public class TransferableQuantityTests
{
    [TestCase(100, 0, 0, 100)]
    [TestCase(100, 40, 0, 60)]
    [TestCase(100, 40, 10, 50)]
    [TestCase(100, 80, 30, 0)]
    [TestCase(0, 0, 0, 0)]
    public void Of_IsQuantityMinusReservedMinusUnsettled_NeverBelowZero(decimal quantity, decimal reserved, decimal unsettled, decimal expected) =>
        TransferableQuantity.Of(quantity, reserved, unsettled).ShouldBe(expected);

    [TestCase(40, 40, true)]
    [TestCase(40.00000001, 40, false)]
    [TestCase(1, 0, false)]
    public void Allows_AQuantityUpToTheTransferable(decimal quantity, decimal transferable, bool expected) =>
        TransferableQuantity.Allows(quantity, transferable).ShouldBe(expected);
}
