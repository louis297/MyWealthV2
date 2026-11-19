using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Tests.Worlds;

/// <summary>Simulator spec R20–R24, R27: the world and its clock, without a database.</summary>
public class WorldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Test]
    public void Create_StartsRunningAtRealTimeWithNoOutage()
    {
        var world = World.Create(FirstId, seed: 7, T0);

        world.WorldId.ShouldBe(FirstId);
        world.Seed.ShouldBe(7);
        world.ClockOffsetSeconds.ShouldBe(0);
        world.Paused.ShouldBeFalse();
        world.PartyOutage.ShouldBeFalse();
        world.StartedAt.ShouldBe(T0);
        world.Now(T0.AddMinutes(5)).ShouldBe(T0.AddMinutes(5));
    }

    [Test]
    public void AdvanceBy_WhileRunning_AddsToTheOffset()
    {
        var world = World.Create(FirstId, 7, T0);

        world.Advance(TimeSpan.FromDays(2), T0);

        world.ClockOffsetSeconds.ShouldBe((long)TimeSpan.FromDays(2).TotalSeconds);
        world.Now(T0.AddHours(1)).ShouldBe(T0.AddDays(2).AddHours(1));
    }

    [Test]
    public void AdvanceBy_WhilePaused_MovesThePausedInstant()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Pause(T0);

        world.Advance(TimeSpan.FromHours(3), T0.AddMinutes(10));

        world.Paused.ShouldBeTrue();
        world.Now(T0.AddDays(1)).ShouldBe(T0.AddHours(3));
    }

    [Test]
    public void AdvanceTo_WhileRunning_SetsTheOffsetSoNowIsTheTarget()
    {
        var world = World.Create(FirstId, 7, T0);
        var target = T0.AddDays(11).AddHours(15);

        world.AdvanceTo(target, T0);

        world.Now(T0).ShouldBe(target);
        world.Now(T0.AddMinutes(1)).ShouldBe(target.AddMinutes(1));
    }

    [Test]
    public void AdvanceTo_WhilePaused_SetsThePausedInstant()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Pause(T0);
        var target = T0.AddDays(3);

        world.AdvanceTo(target, T0.AddHours(1));

        world.Now(T0.AddHours(2)).ShouldBe(target);
    }

    [TestCase(0)]
    [TestCase(-60)]
    public void AdvanceBy_ZeroOrNegative_IsRefused(int seconds)
    {
        var world = World.Create(FirstId, 7, T0);

        var refused = Should.Throw<WorldRuleException>(() => world.Advance(TimeSpan.FromSeconds(seconds), T0));

        refused.Field.ShouldBe("By");
        world.ClockOffsetSeconds.ShouldBe(0);
    }

    [Test]
    public void AdvanceBy_MoreThan366Days_IsRefused()
    {
        var world = World.Create(FirstId, 7, T0);

        Should.Throw<WorldRuleException>(() => world.Advance(TimeSpan.FromDays(366).Add(TimeSpan.FromSeconds(1)), T0))
            .Field.ShouldBe("By");
        Should.NotThrow(() => world.Advance(TimeSpan.FromDays(366), T0));
    }

    [Test]
    public void AdvanceTo_NotAfterNow_IsRefused()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Advance(TimeSpan.FromDays(1), T0);

        Should.Throw<WorldRuleException>(() => world.AdvanceTo(T0.AddHours(1), T0)).Field.ShouldBe("To");
        Should.Throw<WorldRuleException>(() => world.AdvanceTo(T0.AddDays(1), T0)).Field.ShouldBe("To");
        world.Now(T0).ShouldBe(T0.AddDays(1));
    }

    [Test]
    public void AdvanceTo_MoreThan366DaysAhead_IsRefused()
    {
        var world = World.Create(FirstId, 7, T0);

        Should.Throw<WorldRuleException>(() => world.AdvanceTo(T0.AddDays(367), T0)).Field.ShouldBe("To");
        Should.NotThrow(() => world.AdvanceTo(T0.AddDays(366), T0));
    }

    [Test]
    public void Pause_FreezesNow()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Advance(TimeSpan.FromDays(1), T0);

        world.Pause(T0.AddMinutes(30));

        world.Paused.ShouldBeTrue();
        world.Now(T0.AddHours(5)).ShouldBe(T0.AddDays(1).AddMinutes(30));
    }

    [Test]
    public void Resume_ContinuesFromThePausedValueWithoutAJump()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Advance(TimeSpan.FromDays(2), T0);
        world.Pause(T0);

        world.Resume(T0.AddHours(6));

        world.Paused.ShouldBeFalse();
        world.Now(T0.AddHours(6)).ShouldBe(T0.AddDays(2));
        world.Now(T0.AddHours(7)).ShouldBe(T0.AddDays(2).AddHours(1));
    }

    [Test]
    public void Resume_AfterAPauseLongerThanTheOffset_ContinuesAtRealTimeAndNeverGoesBack()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Advance(TimeSpan.FromHours(1), T0);
        world.Pause(T0);

        world.Resume(T0.AddHours(5));

        world.ClockOffsetSeconds.ShouldBe(0);
        world.Now(T0.AddHours(5)).ShouldBe(T0.AddHours(5));
    }

    [Test]
    public void PauseWhilePaused_AndResumeWhileRunning_ChangeNothing()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Resume(T0.AddHours(1));
        world.ClockOffsetSeconds.ShouldBe(0);
        world.Paused.ShouldBeFalse();

        world.Pause(T0.AddHours(1));
        world.Pause(T0.AddHours(2));

        world.Now(T0.AddHours(3)).ShouldBe(T0.AddHours(1));
    }

    [Test]
    public void Reset_StartsANewWorldAtRealTime()
    {
        var world = World.Create(FirstId, 7, T0);
        world.Advance(TimeSpan.FromDays(5), T0);
        world.Pause(T0);
        world.SetOutage(true);
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        world.Reset(secondId, seed: 42, T0.AddHours(2));

        world.WorldId.ShouldBe(secondId);
        world.Seed.ShouldBe(42);
        world.ClockOffsetSeconds.ShouldBe(0);
        world.Paused.ShouldBeFalse();
        world.PartyOutage.ShouldBeFalse();
        world.StartedAt.ShouldBe(T0.AddHours(2));
        world.Now(T0.AddHours(3)).ShouldBe(T0.AddHours(3));
    }

    [Test]
    public void SetOutage_TurnsTheOutageOnAndOff()
    {
        var world = World.Create(FirstId, 7, T0);

        world.SetOutage(true);
        world.PartyOutage.ShouldBeTrue();

        world.SetOutage(false);
        world.PartyOutage.ShouldBeFalse();
    }
}
