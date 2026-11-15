using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class TradeFeeScheduleTests
{
    private static readonly Currency Nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
    private static readonly Currency Jpy = Currency.Create("JPY", "Japanese Yen", 0);
    private static readonly DateOnly CutOff = new(2026, 11, 15);
    private static readonly FeeBand[] OneBand = [new(0m, 0.5m)];
    private static readonly FeeBand[] TwoBands = [new(0m, 0.5m), new(10000m, 0.3m)];

    private static TradeFeeSchedule Schedule(
        IReadOnlyList<FeeBand>? bands = null,
        decimal minimum = 0m,
        decimal? cap = null,
        Currency? currency = null,
        DateOnly? effectiveFrom = null) =>
        TradeFeeSchedule.Create(1, currency ?? Nzd, effectiveFrom ?? CutOff.AddDays(1), minimum, cap, bands ?? OneBand, CutOff);

    [Test]
    public void Create_SetsFields()
    {
        var schedule = Schedule(TwoBands, minimum: 10m, cap: 500m);

        schedule.TenantId.ShouldBe(1);
        schedule.QuoteCurrency.ShouldBe("NZD");
        schedule.EffectiveFrom.ShouldBe(CutOff.AddDays(1));
        schedule.Minimum.ShouldBe(10m);
        schedule.Cap.ShouldBe(500m);
        schedule.Bands.ShouldBe(TwoBands);
        schedule.PublicId.ShouldNotBe(Guid.Empty);
    }

    [TestCase(-0.01, null)]
    [TestCase(0, 0)]
    [TestCase(10, 9.99)]
    [TestCase(10.001, null)]
    [TestCase(0, 100.001)]
    public void Create_InvalidLimits_AreRefused(decimal minimum, decimal? cap)
    {
        Should.Throw<DomainException>(() => Schedule(minimum: minimum, cap: cap));
    }

    [Test]
    public void Create_CapEqualToMinimum_IsAllowed()
    {
        Schedule(minimum: 10m, cap: 10m).Cap.ShouldBe(10m);
    }

    [Test]
    public void Create_InvalidBands_AreRefused()
    {
        Should.Throw<DomainException>(() => Schedule([new FeeBand(5m, 0.5m)]));
    }

    [Test]
    public void Create_EffectiveFromNotAfterCutOff_IsRefused()
    {
        Should.Throw<DomainException>(() => Schedule(effectiveFrom: CutOff));
        Should.Throw<DomainException>(() => Schedule(effectiveFrom: CutOff.AddDays(-30)));
    }

    [Test]
    public void FeeFor_SingleBand()
    {
        var calculation = Schedule().FeeFor(1250m, 2);

        calculation.Banded.ShouldBe(6.25m);
        calculation.LimitApplied.ShouldBeNull();
        calculation.Fee.ShouldBe(6.25m);
    }

    [TestCase(5000, 25)]
    [TestCase(10000, 50)]
    [TestCase(15000, 65)]
    public void FeeFor_TwoBands_BelowOnAndAboveTheEdge(decimal gross, decimal fee)
    {
        Schedule(TwoBands).FeeFor(gross, 2).Fee.ShouldBe(fee);
    }

    [Test]
    public void FeeFor_MinimumBinds()
    {
        var calculation = Schedule(TwoBands, minimum: 10m).FeeFor(1000m, 2);

        calculation.Amount.ShouldBe(1000m);
        calculation.Banded.ShouldBe(5m);
        calculation.LimitApplied.ShouldBe(FeeLimit.Minimum);
        calculation.Fee.ShouldBe(10m);
    }

    [Test]
    public void FeeFor_CapBinds()
    {
        var calculation = Schedule(TwoBands, minimum: 10m, cap: 100m).FeeFor(100000m, 2);

        calculation.Banded.ShouldBe(320m);
        calculation.LimitApplied.ShouldBe(FeeLimit.Cap);
        calculation.Fee.ShouldBe(100m);
    }

    [Test]
    public void FeeFor_NoLimit_WhenBandedIsBetweenMinimumAndCap()
    {
        var calculation = Schedule(TwoBands, minimum: 10m, cap: 100m).FeeFor(15000m, 2);

        calculation.LimitApplied.ShouldBeNull();
        calculation.Fee.ShouldBe(65m);
        calculation.Bands.Count.ShouldBe(2);
        calculation.Bands.Sum(band => band.Fee).ShouldBe(calculation.Banded);
    }

    [TestCase(1234.5678, 6.17)]
    [TestCase(1.01, 0.01)]
    public void FeeFor_RoundsToTwoDecimals_AwayFromZero(decimal gross, decimal fee)
    {
        Schedule().FeeFor(gross, 2).Fee.ShouldBe(fee);
    }

    [TestCase(12345, 62)]
    [TestCase(12300, 62)]
    [TestCase(12100, 61)]
    public void FeeFor_RoundsToZeroDecimals_AwayFromZero(decimal gross, decimal fee)
    {
        Schedule(currency: Jpy).FeeFor(gross, 0).Fee.ShouldBe(fee);
    }

    [Test]
    public void FeeFor_IsNonDecreasingAcrossABandEdge()
    {
        var schedule = Schedule([new FeeBand(0m, 1m), new FeeBand(10000m, 0.1m)], minimum: 10m, cap: 200m);
        var previous = 0m;

        for (var gross = 9990m; gross <= 10010m; gross += 0.5m)
        {
            var fee = schedule.FeeFor(gross, 2).Fee;
            fee.ShouldBeGreaterThanOrEqualTo(previous);
            previous = fee;
        }
    }

    [Test]
    public void Calculate_MatchesFeeFor()
    {
        var schedule = Schedule(TwoBands, minimum: 10m, cap: 100m);

        var calculation = TradeFeeSchedule.Calculate(15000m, 10m, 100m, FeeBands.Create(TwoBands, 2), 2);

        calculation.ShouldBeEquivalentTo(schedule.FeeFor(15000m, 2));
    }

    [Test]
    public void IsInEffect_OnOrBeforeTheCutOff()
    {
        var schedule = Schedule(effectiveFrom: CutOff.AddDays(3));

        schedule.IsInEffect(CutOff).ShouldBeFalse();
        schedule.IsInEffect(CutOff.AddDays(2)).ShouldBeFalse();
        schedule.IsInEffect(CutOff.AddDays(3)).ShouldBeTrue();
        schedule.IsInEffect(CutOff.AddDays(4)).ShouldBeTrue();
    }

    [Test]
    public void Update_Pending_ReplacesValuesAndBands()
    {
        var schedule = Schedule(TwoBands, minimum: 10m);

        schedule.Update(CutOff.AddDays(5), 2m, 50m, OneBand, 2, CutOff);

        schedule.EffectiveFrom.ShouldBe(CutOff.AddDays(5));
        schedule.Minimum.ShouldBe(2m);
        schedule.Cap.ShouldBe(50m);
        schedule.Bands.ShouldBe(OneBand);
        schedule.QuoteCurrency.ShouldBe("NZD");
    }

    [Test]
    public void Update_InEffect_IsRefused()
    {
        var schedule = Schedule(effectiveFrom: CutOff.AddDays(1));

        Should.Throw<TradeFeeScheduleInEffectException>(
            () => schedule.Update(CutOff.AddDays(10), 0m, null, OneBand, 2, CutOff.AddDays(1)));
    }

    [Test]
    public void Update_NewEffectiveFromNotAfterCutOff_IsRefused()
    {
        var schedule = Schedule(effectiveFrom: CutOff.AddDays(5));

        var exception = Should.Throw<DomainException>(
            () => schedule.Update(CutOff, 0m, null, OneBand, 2, CutOff));

        exception.ShouldNotBeOfType<TradeFeeScheduleInEffectException>();
    }

    [Test]
    public void Update_InvalidShape_IsRefused()
    {
        var schedule = Schedule();

        Should.Throw<DomainException>(
            () => schedule.Update(CutOff.AddDays(2), 10m, 5m, OneBand, 2, CutOff));
    }

    [Test]
    public void EnsureDeletable_Pending_Passes_InEffect_IsRefused()
    {
        var schedule = Schedule(effectiveFrom: CutOff.AddDays(1));

        Should.NotThrow(() => schedule.EnsureDeletable(CutOff));
        Should.Throw<TradeFeeScheduleInEffectException>(() => schedule.EnsureDeletable(CutOff.AddDays(1)));
    }
}
