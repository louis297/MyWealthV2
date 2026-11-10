using MyWealthV2.Application.Transactions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Transactions;

/// <summary>FX conversions R10 (staleness) and R11 (caller amount decimal places).</summary>
public class FxConversionPricingTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 9);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    public void IsStale_UpToFourCalendarDays_IsFresh(int days)
    {
        var bookedAt = new DateTimeOffset(2026, 11, 9, 23, 30, 0, TimeSpan.FromHours(13)).AddDays(days);

        FxConversionPricing.IsStale(AsOf, bookedAt).ShouldBeFalse();
    }

    [TestCase(5)]
    [TestCase(30)]
    public void IsStale_MoreThanFourCalendarDays_IsStale(int days)
    {
        var bookedAt = new DateTimeOffset(2026, 11, 9, 0, 30, 0, TimeSpan.FromHours(13)).AddDays(days);

        FxConversionPricing.IsStale(AsOf, bookedAt).ShouldBeTrue();
    }

    [Test]
    public void IsStale_UsesTheCalendarDateOfBookedAtInItsOwnOffset()
    {
        // 2026-11-14 01:00 +13:00 is 2026-11-13 12:00 UTC; the date in its own offset is the 14th: 5 days.
        var bookedAt = new DateTimeOffset(2026, 11, 14, 1, 0, 0, TimeSpan.FromHours(13));

        FxConversionPricing.IsStale(AsOf, bookedAt).ShouldBeTrue();
    }

    [TestCase(1000, 2, true)]
    [TestCase(1000.5, 2, true)]
    [TestCase(1000.55, 2, true)]
    [TestCase(1000.555, 2, false)]
    [TestCase(905, 0, true)]
    [TestCase(905.5, 0, false)]
    [TestCase(1.2345, 4, true)]
    public void FitsDecimalPlaces_ChecksTheScaleOfTheValue(decimal amount, int decimalPlaces, bool expected)
    {
        FxConversionPricing.FitsDecimalPlaces(amount, decimalPlaces).ShouldBe(expected);
    }

    [Test]
    public void FitsDecimalPlaces_IgnoresTrailingZeros()
    {
        FxConversionPricing.FitsDecimalPlaces(1000.5000m, 2).ShouldBeTrue();
        FxConversionPricing.FitsDecimalPlaces(905.000m, 0).ShouldBeTrue();
    }
}
