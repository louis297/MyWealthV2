using MyWealthV2.Domain.Custody;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Custody;

/// <summary>Orders R40: available quantity.</summary>
public class AvailableQuantityTests
{
    [TestCase(100, 40, 60)]
    [TestCase(100, 0, 100)]
    [TestCase(40, 40, 0)]
    [TestCase(0.5, 0.12345678, 0.37654322)]
    public void Of_IsTheQuantityMinusReserved(decimal quantity, decimal reserved, decimal available) =>
        AvailableQuantity.Of(quantity, reserved).ShouldBe(available);

    [TestCase(60, 40, true)]
    [TestCase(40, 40, true)]
    [TestCase(39.99999999, 40, false)]
    [TestCase(0, 0, true)]
    public void Allows_WhenTheQuantityAfterTheWriteCoversTheReservation(decimal after, decimal reserved, bool allowed) =>
        AvailableQuantity.Allows(after, reserved).ShouldBe(allowed);
}
