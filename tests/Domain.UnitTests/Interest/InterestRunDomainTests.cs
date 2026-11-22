using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Interest;

/// <summary>Interest-run R21, R30: the interest run and its posting date.</summary>
public class InterestRunDomainTests
{
    private static readonly DateTimeOffset At = new(2027, 1, 1, 0, 30, 2, TimeSpan.Zero);

    [TestCase("2026-12-31", "2026-12-31")]
    [TestCase("2027-01-15", "2026-12-31")]
    [TestCase("2027-01-01", "2026-12-31")]
    [TestCase("2027-02-28", "2027-02-28")]
    [TestCase("2028-02-28", "2028-01-31")]
    [TestCase("2028-02-29", "2028-02-29")]
    [TestCase("2026-11-30", "2026-11-30")]
    public void PostingDate_IsTheLastDayOfTheLatestMonthEndedByTheAsOfDate(string asOf, string posting)
    {
        InterestRun.PostingDateFor(DateOnly.Parse(asOf)).ShouldBe(DateOnly.Parse(posting));
        InterestRun.Start(1, DateOnly.Parse(asOf), InterestRunTrigger.Scheduled, null, null, At).PostingDate
            .ShouldBe(DateOnly.Parse(posting));
    }

    [Test]
    public void Start_SetsFields_WithZeroCounts_AndNoEnd()
    {
        var run = InterestRun.Start(1, new DateOnly(2026, 12, 31), InterestRunTrigger.Manual, 7, null, At);

        (run.TenantId, run.AsOfDate, run.Trigger, run.RanByUserId, run.AccountId, run.StartedAt)
            .ShouldBe((1, new DateOnly(2026, 12, 31), InterestRunTrigger.Manual, (int?)7, (int?)null, At));
        run.PublicId.ShouldNotBe(Guid.Empty);
        run.CompletedAt.ShouldBeNull();
        (run.AccountsProcessed, run.AccountsFailed, run.PostingCount).ShouldBe((0, 0, 0));
    }

    [TestCase(InterestRunTrigger.Scheduled, 7, null)]
    [TestCase(InterestRunTrigger.Manual, null, null)]
    [TestCase(InterestRunTrigger.AccountClosure, 7, null)]
    [TestCase(InterestRunTrigger.Manual, 7, 11)]
    [TestCase(InterestRunTrigger.Scheduled, null, 11)]
    public void Start_AShapeOutsideTheTrigger_IsRefused(InterestRunTrigger trigger, int? ranBy, int? account)
    {
        Should.Throw<DomainException>(() => InterestRun.Start(1, new DateOnly(2026, 12, 31), trigger, ranBy, account, At));
    }

    [Test]
    public void Start_AnAccountClosureRun_NamesThePersonAndTheAccount()
    {
        var run = InterestRun.Start(1, new DateOnly(2026, 12, 14), InterestRunTrigger.AccountClosure, 7, 11, At);

        (run.RanByUserId, run.AccountId).ShouldBe(((int?)7, (int?)11));
    }

    [Test]
    public void Complete_SetsTheCountsOnce()
    {
        var run = InterestRun.Start(1, new DateOnly(2026, 12, 31), InterestRunTrigger.Scheduled, null, null, At);

        run.Complete(42, 1, 37, At.AddSeconds(7));

        (run.AccountsProcessed, run.AccountsFailed, run.PostingCount, run.CompletedAt).ShouldBe((42, 1, 37, (DateTimeOffset?)At.AddSeconds(7)));
        Should.Throw<DomainException>(() => run.Complete(1, 0, 0, At.AddSeconds(9)));
        run.AccountsProcessed.ShouldBe(42);
    }

    [Test]
    public void Complete_NegativeCounts_OrMoreFailedThanProcessed_AreRefused()
    {
        var run = InterestRun.Start(1, new DateOnly(2026, 12, 31), InterestRunTrigger.Scheduled, null, null, At);

        Should.Throw<DomainException>(() => run.Complete(-1, 0, 0, At));
        Should.Throw<DomainException>(() => run.Complete(1, 2, 0, At));
        run.CompletedAt.ShouldBeNull();
    }
}
