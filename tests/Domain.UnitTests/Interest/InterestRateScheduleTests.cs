using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Interest;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Interest;

/// <summary>Interest-run R9–R14, R17, R18: the interest rate schedule and one day's accrual.</summary>
public class InterestRateScheduleTests
{
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);
    private static readonly Currency Jpy = Currency.Create("JPY", "Japanese Yen", 0);
    private static readonly DateOnly CutOff = new(2026, 11, 22);
    private static readonly FeeBand[] ThreePercent = [new(0m, 3m)];
    private static readonly FeeBand[] TwoBands = [new(0m, 3m), new(100000m, 2m)];

    private static InterestRateSchedule Schedule(IReadOnlyList<FeeBand>? bands = null, Currency? currency = null, DateOnly? from = null) =>
        InterestRateSchedule.Create(1, currency ?? Usd, from ?? CutOff.AddDays(9), bands ?? ThreePercent, CutOff);

    [Test]
    public void Create_SetsFields()
    {
        var schedule = Schedule(TwoBands);

        schedule.TenantId.ShouldBe(1);
        schedule.Currency.ShouldBe("USD");
        schedule.EffectiveFrom.ShouldBe(new DateOnly(2026, 12, 1));
        schedule.Bands.ShouldBe(TwoBands);
        schedule.PublicId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void Create_EffectiveFromNotAfterTheCutOff_IsRefused()
    {
        Should.Throw<DomainException>(() => Schedule(from: CutOff));
        Should.Throw<DomainException>(() => Schedule(from: CutOff.AddDays(-1)));
    }

    [Test]
    public void Create_ABandShapeOutsideR9_IsRefused()
    {
        Should.Throw<DomainException>(() => Schedule([]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(10m, 3m)]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(0m, 3m), new FeeBand(0m, 2m)]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(0m, 100.0001m)]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(0m, -1m)]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(0m, 3.00001m)]));
        Should.Throw<DomainException>(() => Schedule([new FeeBand(0m, 3m), new FeeBand(1000.5m, 2m)], Jpy));
        Should.Throw<DomainException>(() => Schedule(Enumerable.Range(0, 11).Select(i => new FeeBand(i * 10m, 1m)).ToArray()));
    }

    [Test]
    public void Create_AZeroRate_IsAllowed()
    {
        Schedule([new FeeBand(0m, 0m)]).Bands.Single().RatePercent.ShouldBe(0m);
    }

    [Test]
    public void IsInEffect_OnAndAfterTheCutOff()
    {
        var schedule = Schedule();

        schedule.IsInEffect(CutOff).ShouldBeFalse();
        schedule.IsInEffect(new DateOnly(2026, 11, 30)).ShouldBeFalse();
        schedule.IsInEffect(new DateOnly(2026, 12, 1)).ShouldBeTrue();
        schedule.IsInEffect(new DateOnly(2027, 1, 1)).ShouldBeTrue();
    }

    [Test]
    public void Update_APendingRow_ReplacesTheDateAndEveryBand()
    {
        var schedule = Schedule(TwoBands);

        schedule.Update(new DateOnly(2027, 1, 1), ThreePercent, 2, CutOff);

        schedule.EffectiveFrom.ShouldBe(new DateOnly(2027, 1, 1));
        schedule.Bands.ShouldBe(ThreePercent);
        schedule.Currency.ShouldBe("USD");
    }

    [Test]
    public void Update_ARowInEffect_IsRefused_AndChangesNothing()
    {
        var schedule = Schedule(TwoBands);

        Should.Throw<InterestScheduleInEffectException>(() =>
            schedule.Update(new DateOnly(2027, 1, 1), ThreePercent, 2, new DateOnly(2026, 12, 1)));
        schedule.EffectiveFrom.ShouldBe(new DateOnly(2026, 12, 1));
        schedule.Bands.ShouldBe(TwoBands);
    }

    [Test]
    public void Update_ANewDateNotAfterTheCutOff_IsRefused()
    {
        Should.Throw<DomainException>(() => Schedule().Update(CutOff, ThreePercent, 2, CutOff));
    }

    [Test]
    public void EnsureDeletable_APendingRow_Passes_AndARowInEffect_Throws()
    {
        var schedule = Schedule();

        schedule.EnsureDeletable(CutOff);
        Should.Throw<InterestScheduleInEffectException>(() => schedule.EnsureDeletable(new DateOnly(2026, 12, 1)));
    }

    [Test]
    public void AccrualFor_OneBand_5000At3Percent()
    {
        Schedule().AccrualFor(5000m).ShouldBe(new DayAccrual(0.41095890m, 3.0000m));
    }

    [TestCase(50000, 4.10958904, 3)]
    [TestCase(100000, 8.21917808, 3)]
    [TestCase(150000, 10.95890411, 2.6667)]
    public void AccrualFor_TwoBands_BelowOnAndAboveTheEdge_WithTheBlendedRate(decimal balance, decimal amount, decimal rate)
    {
        Schedule(TwoBands).AccrualFor(balance).ShouldBe(new DayAccrual(amount, rate));
    }

    [Test]
    public void AccrualFor_AZeroRate_AndAZeroBalance_AreZero()
    {
        Schedule([new FeeBand(0m, 0m)]).AccrualFor(5000m).Amount.ShouldBe(0m);
        Schedule().AccrualFor(0m).ShouldBe(new DayAccrual(0m, 0m));
    }

    [TestCase(1, 0.00008219)]
    [TestCase(0.01, 0.00000082)]
    [TestCase(1234.5678, 0.10147133)]
    public void AccrualFor_RoundsTo8Decimals_AwayFromZero(decimal balance, decimal amount)
    {
        Schedule().AccrualFor(balance).Amount.ShouldBe(amount);
    }
}
