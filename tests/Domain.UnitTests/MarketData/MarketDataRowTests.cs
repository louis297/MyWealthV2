using MyWealthV2.Domain.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.MarketData;

/// <summary>Market data history R4, R5: a row is final from the second day after its date.</summary>
public class MarketDataRowTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Test]
    public void Today_IsNotFinal() => MarketDataRow.IsFinal(Today, Today).ShouldBeFalse();

    [Test]
    public void Yesterday_IsNotFinal() => MarketDataRow.IsFinal(Today.AddDays(-1), Today).ShouldBeFalse();

    [Test]
    public void TwoDaysAgo_IsFinal() => MarketDataRow.IsFinal(Today.AddDays(-2), Today).ShouldBeTrue();

    [Test]
    public void AYearAgo_IsFinal() => MarketDataRow.IsFinal(Today.AddYears(-1), Today).ShouldBeTrue();

    [Test]
    public void ADateAfterToday_IsNotFinal() => MarketDataRow.IsFinal(Today.AddDays(1), Today).ShouldBeFalse();
}
