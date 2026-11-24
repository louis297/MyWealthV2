using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R51–R55: <c>CustodianReconciliation.Compute</c>.</summary>
public class CustodianReconciliationTests
{
    private const int Acme = 9;
    private const int Beta = 10;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 2, 0, 0, TimeSpan.Zero);

    [Test]
    public void NoStoredPositions_IsNoPositions_WithNoLine()
    {
        var run = Compute(Inputs(noPositions: true, platform: [new(Acme, "ACME", 100m)]));

        run.Status.ShouldBe(CustodianReconciliationStatus.NoPositions);
        run.Lines.ShouldBeEmpty();
        run.PositionsAt.ShouldBeNull();
    }

    [Test]
    public void EqualQuantities_AreAnAgreedLine_AndTheRunIsAgreed()
    {
        var run = Compute(Inputs(positions: [new("acme", 100m)], platform: [new(Acme, "ACME", 100m)]));

        run.Status.ShouldBe(CustodianReconciliationStatus.Agreed);
        var line = run.Lines.ShouldHaveSingleItem();
        (line.Symbol, line.InstrumentId, line.CustodianQuantity, line.PlatformQuantity, line.Difference)
            .ShouldBe(("ACME", (int?)Acme, 100m, 100m, 0m));
        line.Class.ShouldBe(CustodianLineClass.Agreed);
        (run.BreakCount, run.TimingCount, run.AgreedCount).ShouldBe((0, 0, 1));
        run.PositionsAt.ShouldBe(At.AddMinutes(-1));
    }

    [Test]
    public void APositionWithNoInstrument_IsABreakLine_WithNoInstrument()
    {
        var run = Compute(Inputs(positions: [new("OTHER", 5m)]));

        var line = run.Lines.ShouldHaveSingleItem();
        (line.Symbol, line.InstrumentId, line.PlatformQuantity, line.Unexplained, line.Class)
            .ShouldBe(("OTHER", (int?)null, 0m, 5m, CustodianLineClass.Break));
        run.Status.ShouldBe(CustodianReconciliationStatus.Breaks);
    }

    [Test]
    public void AHoldingTheCustodianDoesNotHold_IsAnUnexplainedBreak()
    {
        var line = Compute(Inputs(platform: [new(Acme, "ACME", 1200m)])).Lines.ShouldHaveSingleItem();

        (line.CustodianQuantity, line.PlatformQuantity, line.Difference, line.Unexplained).ShouldBe((0m, 1200m, -1200m, -1200m));
        line.Class.ShouldBe(CustodianLineClass.Break);
    }

    [TestCase(10, 10)]
    [TestCase(-10, -10)]
    public void AReceivedMovement_IsNotProcessed_AndTiming(decimal signed, decimal expected)
    {
        var line = Compute(Inputs(positions: [new("ACME", 100m + signed)], platform: [new(Acme, "ACME", 100m)],
            notProcessed: [new("ACME", signed)])).Lines.ShouldHaveSingleItem();

        (line.NotProcessed, line.Unexplained, line.Class).ShouldBe((expected, 0m, CustodianLineClass.Timing));
    }

    [TestCase(10, -10)]
    [TestCase(-10, 10)]
    public void APostedFillNotDelivered_IsUnsettledTrades_WithTheOppositeSign(decimal signedFill, decimal expected)
    {
        var line = Compute(Inputs(positions: [new("ACME", 50m)], platform: [new(Acme, "ACME", 50m + signedFill)],
            fillsNotDelivered: [new(Acme, "ACME", signedFill)])).Lines.ShouldHaveSingleItem();

        (line.Difference, line.UnsettledTrades, line.Unexplained, line.Class).ShouldBe((-signedFill, expected, 0m, CustodianLineClass.Timing));
    }

    [TestCase(800, 800)]
    [TestCase(-5, -5)]
    public void AnUnmatchedMovement_IsSuspense_AndABreak(decimal signed, decimal expected)
    {
        var line = Compute(Inputs(positions: [new("ACME", 100m + signed)], platform: [new(Acme, "ACME", 100m)],
            suspense: [new("ACME", signed)])).Lines.ShouldHaveSingleItem();

        (line.Suspense, line.Unexplained, line.Class).ShouldBe((expected, 0m, CustodianLineClass.Break));
    }

    [TestCase(10, 10)]
    [TestCase(-10, -10)]
    public void ADeliveredFillStillPending_IsUnbookedFills_AndABreak(decimal signed, decimal expected)
    {
        var line = Compute(Inputs(positions: [new("ACME", 100m + signed)], platform: [new(Acme, "ACME", 100m)],
            delivered: [new(Acme, "ACME", signed)])).Lines.ShouldHaveSingleItem();

        (line.UnbookedFills, line.Unexplained, line.Class).ShouldBe((expected, 0m, CustodianLineClass.Break));
    }

    [Test]
    public void TheComponentsAddUpToTheDifference_AndTheRestIsUnexplained()
    {
        var line = Compute(Inputs(positions: [new("ACME", 107m)], platform: [new(Acme, "ACME", 100m)],
            notProcessed: [new("ACME", 2m)], fillsNotDelivered: [new(Acme, "ACME", 3m)], suspense: [new("ACME", 4m)],
            delivered: [new(Acme, "ACME", 1m)])).Lines.ShouldHaveSingleItem();

        line.Difference.ShouldBe(7m);
        (line.NotProcessed, line.UnsettledTrades, line.Suspense, line.UnbookedFills).ShouldBe((2m, -3m, 4m, 1m));
        line.Unexplained.ShouldBe(3m);
        (line.NotProcessed + line.UnsettledTrades + line.Suspense + line.UnbookedFills + line.Unexplained).ShouldBe(line.Difference);
    }

    [Test]
    public void AnInstrumentWithNothingOnEitherSide_HasNoLine()
    {
        var run = Compute(Inputs(positions: [new("ACME", 1m)], platform: [new(Acme, "ACME", 1m), new(Beta, "BETA", 0m)]));

        run.Lines.Select(line => line.Symbol).ShouldBe(["ACME"]);
    }

    [Test]
    public void Lines_AreOrderedByClassThenSymbol_AndCountedPerClass()
    {
        var run = Compute(Inputs(
            positions: [new("ZED", 1m), new("ACME", 1m), new("BETA", 6m), new("ALPHA", 2m)],
            platform: [new(Acme, "ACME", 1m), new(Beta, "BETA", 5m), new(11, "ALPHA", 2m)],
            notProcessed: [new("BETA", 1m)]));

        run.Lines.Select(line => (line.Position, line.Symbol, line.Class)).ShouldBe([
            (1, "ZED", CustodianLineClass.Break),
            (2, "BETA", CustodianLineClass.Timing),
            (3, "ACME", CustodianLineClass.Agreed),
            (4, "ALPHA", CustodianLineClass.Agreed)
        ]);
        (run.BreakCount, run.TimingCount, run.AgreedCount).ShouldBe((1, 1, 2));
    }

    [Test]
    public void TheRun_KeepsTheTriggerThePersonTheInformationAndTheFeedStatus()
    {
        var run = CustodianReconciliation.Compute(Inputs(positions: [new("ACME", 1m)], platform: [new(Acme, "ACME", 1m)]) with
        {
            TransfersInFlight = 2,
            FillsNotDeliveredCount = 3,
            FeedLastFailure = CustodianFailure.Timeout,
            FeedLastFailedAt = At
        }, ReconciliationTrigger.Manual, 22, At);

        (run.TenantId, run.SafekeepingAccountId, run.Currency).ShouldBe((3, 4, "USD"));
        (run.Trigger, run.RanByUserId, run.RanAt).ShouldBe((ReconciliationTrigger.Manual, (int?)22, At));
        (run.TransfersInFlight, run.FillsNotDelivered).ShouldBe((2, 3));
        (run.FeedLastFailure, run.FeedLastFailedAt).ShouldBe(((CustodianFailure?)CustodianFailure.Timeout, (DateTimeOffset?)At));
        run.PublicId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void AScheduledRun_NamesNoPerson() =>
        CustodianReconciliation.Compute(Inputs(), ReconciliationTrigger.Scheduled, null, At).RanByUserId.ShouldBeNull();

    private static CustodianReconciliation Compute(CustodianReconciliationInputs inputs) =>
        CustodianReconciliation.Compute(inputs, ReconciliationTrigger.Scheduled, null, At);

    private static CustodianReconciliationInputs Inputs(
        IReadOnlyList<CustodianSymbolQuantity>? positions = null,
        IReadOnlyList<CustodianInstrumentQuantity>? platform = null,
        IReadOnlyList<CustodianSymbolQuantity>? notProcessed = null,
        IReadOnlyList<CustodianSymbolQuantity>? suspense = null,
        IReadOnlyList<CustodianInstrumentQuantity>? fillsNotDelivered = null,
        IReadOnlyList<CustodianInstrumentQuantity>? delivered = null,
        bool noPositions = false) => new()
    {
        TenantId = 3,
        SafekeepingAccountId = 4,
        Currency = "USD",
        PositionsAt = noPositions ? null : At.AddMinutes(-1),
        Positions = positions ?? [],
        Platform = platform ?? [],
        NotProcessed = notProcessed ?? [],
        Suspense = suspense ?? [],
        FillsNotDelivered = fillsNotDelivered ?? [],
        DeliveredPendingFills = delivered ?? [],
        TransfersInFlight = 0,
        FillsNotDeliveredCount = 0
    };
}
