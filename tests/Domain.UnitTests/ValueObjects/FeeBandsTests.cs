using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

public class FeeBandsTests
{
    private static readonly FeeBand[] TwoBands = [new(0m, 0.5m), new(10000m, 0.3m)];

    [Test]
    public void Create_KeepsBandsInOrder()
    {
        var bands = FeeBands.Create(TwoBands, 2);

        bands.Items.ShouldBe(TwoBands);
    }

    [Test]
    public void Create_AcceptsTenBands()
    {
        var bands = Enumerable.Range(0, FeeBands.MaxBands).Select(index => new FeeBand(index * 100m, 1m)).ToArray();

        FeeBands.Create(bands, 2).Items.Count.ShouldBe(10);
    }

    [Test]
    public void Create_AcceptsZeroAndHundredPercentAndFourDecimals()
    {
        FeeBand[] bands = [new(0m, 0m), new(100m, 100m), new(200m, 0.1234m)];

        FeeBands.Create(bands, 2).Items.Count.ShouldBe(3);
    }

    [Test]
    public void Create_AcceptsTrailingZerosBeyondTheCurrencyDecimals()
    {
        FeeBand[] bands = [new(0m, 0.5m), new(10000.000m, 0.3m)];

        FeeBands.Create(bands, 2).Items.Count.ShouldBe(2);
    }

    private static IEnumerable<TestCaseData> InvalidShapes()
    {
        yield return new TestCaseData(new FeeBand[0], 2).SetName("No bands");
        yield return new TestCaseData(
            Enumerable.Range(0, 11).Select(index => new FeeBand(index * 100m, 1m)).ToArray(), 2).SetName("Eleven bands");
        yield return new TestCaseData(new[] { new FeeBand(1m, 0.5m) }, 2).SetName("First from is not 0");
        yield return new TestCaseData(new[] { new FeeBand(0m, 0.5m), new FeeBand(0m, 0.3m) }, 2).SetName("From repeated");
        yield return new TestCaseData(
            new[] { new FeeBand(0m, 0.5m), new FeeBand(200m, 0.3m), new FeeBand(100m, 0.2m) }, 2).SetName("From descending");
        yield return new TestCaseData(new[] { new FeeBand(0m, -0.1m) }, 2).SetName("Negative rate");
        yield return new TestCaseData(new[] { new FeeBand(0m, 100.0001m) }, 2).SetName("Rate above 100");
        yield return new TestCaseData(new[] { new FeeBand(0m, 0.12345m) }, 2).SetName("Rate with five decimals");
        yield return new TestCaseData(new[] { new FeeBand(0m, 0.5m), new FeeBand(100.001m, 0.3m) }, 2)
            .SetName("From with more decimals than the currency");
        yield return new TestCaseData(new[] { new FeeBand(0m, 0.5m), new FeeBand(100.5m, 0.3m) }, 0)
            .SetName("From with decimals for a zero-decimal currency");
    }

    [TestCaseSource(nameof(InvalidShapes))]
    public void Create_InvalidShape_IsRefused(FeeBand[] bands, int decimalPlaces)
    {
        Should.Throw<DomainException>(() => FeeBands.Create(bands, decimalPlaces));
    }

    [Test]
    public void Slices_CountOnlyBandsStartingBelowTheAmount()
    {
        var bands = FeeBands.Create(TwoBands, 2);

        bands.Slices(10000m).ShouldBe([new FeeBandSlice(0m, 0.5m, 10000m, 50m)]);
    }

    [Test]
    public void Slices_SplitTheAmountAtTheBandEdge()
    {
        var bands = FeeBands.Create(TwoBands, 2);

        bands.Slices(15000m).ShouldBe(
        [
            new FeeBandSlice(0m, 0.5m, 10000m, 50m),
            new FeeBandSlice(10000m, 0.3m, 5000m, 15m)
        ]);
    }

    [TestCase(1)]
    [TestCase(9999.99)]
    [TestCase(10000)]
    [TestCase(10000.01)]
    [TestCase(123456.7891)]
    public void Slices_SumToTheAmount_AndFeesSumToTheBandedAmount(decimal amount)
    {
        var bands = FeeBands.Create([new(0m, 0.5m), new(10000m, 0.3m), new(50000m, 0.1m)], 2);

        var slices = bands.Slices(amount);

        slices.Sum(slice => slice.Slice).ShouldBe(amount);
        slices.Sum(slice => slice.Fee).ShouldBe(bands.Apply(amount));
    }

    [Test]
    public void Apply_IsExact_NotRounded()
    {
        var bands = FeeBands.Create([new(0m, 0.5m)], 2);

        bands.Apply(1234.5678m).ShouldBe(6.172839m);
    }
}
