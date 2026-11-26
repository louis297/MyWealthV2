using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Valuations;

/// <summary>Valuation-run R13: the stored run and its counts.</summary>
public class ValuationRunTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 6, 13, 0, 1, TimeSpan.Zero);
    private static readonly DateOnly Through = new(2026, 3, 4);

    [Test]
    public void Start_SetsFields_WithZeroCounts_AndNoEnd()
    {
        var run = ValuationRun.Start(1, Through, ValuationRunTrigger.Manual, 7, At);

        (run.TenantId, run.ThroughDate, run.Trigger, run.RanByUserId, run.StartedAt)
            .ShouldBe((1, Through, ValuationRunTrigger.Manual, (int?)7, At));
        run.PublicId.ShouldNotBe(Guid.Empty);
        run.CompletedAt.ShouldBeNull();
        (run.AccountsProcessed, run.AccountsFailed, run.DaysValued, run.DaysIncomplete).ShouldBe((0, 0, 0, 0));
    }

    [TestCase(ValuationRunTrigger.Scheduled, 7)]
    [TestCase(ValuationRunTrigger.Manual, null)]
    [TestCase((ValuationRunTrigger)2, 7)]
    public void Start_WithAPersonThatDoesNotFitTheTrigger_IsRefused(ValuationRunTrigger trigger, int? ranBy)
    {
        Should.Throw<DomainException>(() => ValuationRun.Start(1, Through, trigger, ranBy, At));
    }

    [Test]
    public void Complete_SetsTheCounts_Once()
    {
        var run = ValuationRun.Start(1, Through, ValuationRunTrigger.Scheduled, null, At);

        run.Complete(1, 0, 2, 0, At.AddSeconds(2));

        (run.AccountsProcessed, run.AccountsFailed, run.DaysValued, run.DaysIncomplete).ShouldBe((1, 0, 2, 0));
        run.CompletedAt.ShouldBe(At.AddSeconds(2));
        Should.Throw<DomainException>(() => run.Complete(1, 0, 2, 0, At.AddSeconds(3)));
        run.CompletedAt.ShouldBe(At.AddSeconds(2));
    }

    [TestCase(-1, 0, 0, 0)]
    [TestCase(1, -1, 0, 0)]
    [TestCase(1, 2, 0, 0)]
    [TestCase(1, 0, -1, 0)]
    [TestCase(1, 0, 2, 3)]
    [TestCase(1, 0, 2, -1)]
    public void Complete_WithCountsThatDoNotFit_IsRefused(int processed, int failed, int days, int incomplete)
    {
        var run = ValuationRun.Start(1, Through, ValuationRunTrigger.Scheduled, null, At);

        Should.Throw<DomainException>(() => run.Complete(processed, failed, days, incomplete, At));
        run.CompletedAt.ShouldBeNull();
    }
}
