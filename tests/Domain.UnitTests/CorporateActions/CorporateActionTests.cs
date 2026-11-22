using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Corporate-actions R5, R6, R12, R18–R22, §5: the status moves of a corporate action and its origin.</summary>
public class CorporateActionTests
{
    private static readonly DateOnly ExDate = new(2026, 12, 1);
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 41, 0, TimeSpan.Zero);

    private static CorporateAction Manual() =>
        CorporateAction.Enter(1, 10, CorporateActionKind.Split, new CorporateActionRatio(4, 1), ExDate, null, null, "  Announced by the issuer  ");

    private static CorporateAction Feed(int version = 1) =>
        CorporateAction.Announce(1, 10, "simulator", "w-CA1", version, CorporateActionKind.Split, new CorporateActionRatio(2, 1),
            ExDate, new DateOnly(2026, 12, 2), null);

    [Test]
    public void Enter_IsManualAndAnnounced_WithTheTermsAndATrimmedMemo()
    {
        var action = Manual();

        action.PublicId.ShouldNotBe(Guid.Empty);
        (action.TenantId, action.InstrumentId, action.Kind, action.RatioNew, action.RatioHeld, action.ExDate)
            .ShouldBe((1, 10, CorporateActionKind.Split, 4, 1, ExDate));
        action.Origin.ShouldBe(CorporateActionOrigin.Manual);
        action.Status.ShouldBe(CorporateActionStatus.Announced);
        action.Memo.ShouldBe("Announced by the issuer");
        action.Connection.ShouldBeNull();
        action.PartyActionId.ShouldBeNull();
        action.FeedVersion.ShouldBeNull();
        action.Ratio.ShouldBe(new CorporateActionRatio(4, 1));
    }

    [Test]
    public void Enter_ARatioThatDoesNotFitTheKind_Throws() =>
        Should.Throw<DomainException>(() =>
            CorporateAction.Enter(1, 10, CorporateActionKind.Consolidation, new CorporateActionRatio(2, 1), ExDate, null, null, null));

    [TestCase("")]
    [TestCase("   ")]
    public void Enter_AnEmptyMemo_Throws(string memo) =>
        Should.Throw<DomainException>(() =>
            CorporateAction.Enter(1, 10, CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null, memo));

    [Test]
    public void Enter_AMemoOver200_Throws() =>
        Should.Throw<DomainException>(() =>
            CorporateAction.Enter(1, 10, CorporateActionKind.Split, new CorporateActionRatio(2, 1), ExDate, null, null, new string('m', 201)));

    [Test]
    public void Announce_IsFeedAndAnnounced_WithTheConnectionPartyIdAndVersion()
    {
        var action = Feed(3);

        action.Origin.ShouldBe(CorporateActionOrigin.Feed);
        action.Status.ShouldBe(CorporateActionStatus.Announced);
        (action.Connection, action.PartyActionId, action.FeedVersion).ShouldBe(("simulator", "w-CA1", (int?)3));
        action.RecordDate.ShouldBe(new DateOnly(2026, 12, 2));
        action.Memo.ShouldBeNull();
    }

    [Test]
    public void Change_ReplacesKindRatioDatesAndMemo_AndKeepsTheInstrument()
    {
        var action = Manual();

        action.Change(CorporateActionKind.Bonus, new CorporateActionRatio(1, 5), ExDate.AddDays(1), ExDate.AddDays(2), ExDate.AddDays(3), null);

        (action.Kind, action.RatioNew, action.RatioHeld, action.ExDate, action.RecordDate, action.PaymentDate, action.Memo)
            .ShouldBe((CorporateActionKind.Bonus, 1, 5, ExDate.AddDays(1), (DateOnly?)ExDate.AddDays(2), (DateOnly?)ExDate.AddDays(3), (string?)null));
        action.InstrumentId.ShouldBe(10);
        action.Status.ShouldBe(CorporateActionStatus.Announced);
    }

    [Test]
    public void Change_ARatioThatDoesNotFitTheNewKind_Throws_AndChangesNothing()
    {
        var action = Manual();

        Should.Throw<DomainException>(() =>
            action.Change(CorporateActionKind.Consolidation, new CorporateActionRatio(4, 1), ExDate, null, null, null));

        (action.Kind, action.RatioNew, action.RatioHeld).ShouldBe((CorporateActionKind.Split, 4, 1));
    }

    [Test]
    public void Change_OnAFeedAction_Throws() =>
        Should.Throw<DomainException>(() =>
            Feed().Change(CorporateActionKind.Split, new CorporateActionRatio(3, 1), ExDate, null, null, null));

    [Test]
    public void Amend_ALaterVersion_ReplacesRatioAndDates()
    {
        var action = Feed();

        action.Amend(2, new CorporateActionRatio(3, 1), ExDate.AddDays(1), null, ExDate.AddDays(5)).ShouldBeTrue();

        (action.RatioNew, action.RatioHeld, action.ExDate, action.RecordDate, action.PaymentDate, action.FeedVersion)
            .ShouldBe((3, 1, ExDate.AddDays(1), (DateOnly?)null, (DateOnly?)ExDate.AddDays(5), (int?)2));
        action.Kind.ShouldBe(CorporateActionKind.Split);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Amend_AVersionNotAboveTheStoredOne_ChangesNothing(int version)
    {
        var action = Feed(2);

        action.Amend(version, new CorporateActionRatio(3, 1), ExDate.AddDays(1), null, null).ShouldBeFalse();

        (action.RatioNew, action.ExDate, action.FeedVersion).ShouldBe((2, ExDate, (int?)2));
    }

    [Test]
    public void Amend_OnAManualAction_Throws() =>
        Should.Throw<DomainException>(() => Manual().Amend(2, new CorporateActionRatio(3, 1), ExDate, null, null));

    [Test]
    public void Cancel_AManualAction_KeepsThePersonTheTimeAndTheNote()
    {
        var action = Manual();

        action.Cancel(7, "  Entered for the wrong instrument ", At);

        action.Status.ShouldBe(CorporateActionStatus.Cancelled);
        (action.CancelledAt, action.CancelledByUserId, action.CancelNote)
            .ShouldBe(((DateTimeOffset?)At, (int?)7, "Entered for the wrong instrument"));
    }

    [TestCase("")]
    [TestCase("  ")]
    public void Cancel_WithoutANote_Throws(string note)
    {
        var action = Manual();

        Should.Throw<DomainException>(() => action.Cancel(7, note, At));
        action.Status.ShouldBe(CorporateActionStatus.Announced);
    }

    [Test]
    public void Cancel_OnAFeedAction_Throws() =>
        Should.Throw<DomainException>(() => Feed().Cancel(7, "No", At));

    [Test]
    public void CancelFromFeed_ALaterVersion_CancelsWithNoPersonOrNote()
    {
        var action = Feed();

        action.CancelFromFeed(2, At).ShouldBeTrue();

        action.Status.ShouldBe(CorporateActionStatus.Cancelled);
        (action.CancelledAt, action.CancelledByUserId, action.CancelNote, action.FeedVersion)
            .ShouldBe(((DateTimeOffset?)At, (int?)null, (string?)null, (int?)2));
    }

    [Test]
    public void CancelFromFeed_AnOlderVersion_ChangesNothing()
    {
        var action = Feed(3);

        action.CancelFromFeed(2, At).ShouldBeFalse();

        action.Status.ShouldBe(CorporateActionStatus.Announced);
    }

    [Test]
    public void CancelFromFeed_OnAManualAction_Throws() =>
        Should.Throw<DomainException>(() => Manual().CancelFromFeed(2, At));

    [Test]
    public void MakeEffective_ByThePlatform_SetsEffectiveAt()
    {
        var action = Feed();

        action.MakeEffective(At);

        action.Status.ShouldBe(CorporateActionStatus.Effective);
        action.EffectiveAt.ShouldBe(At);
        action.EffectiveByUserId.ShouldBeNull();
    }

    [Test]
    public void MakeEffective_ByAPerson_KeepsThePerson()
    {
        var action = Manual();

        action.MakeEffective(At, 7);

        action.Status.ShouldBe(CorporateActionStatus.Effective);
        action.EffectiveByUserId.ShouldBe(7);
    }

    [Test]
    public void MakeEffective_AFeedActionByAPerson_Throws() =>
        Should.Throw<DomainException>(() => Feed().MakeEffective(At, 7));

    [Test]
    public void MarkApplied_AfterEffective_IsApplied()
    {
        var action = Manual();
        action.MakeEffective(At);

        action.MarkApplied(At.AddSeconds(2));

        action.Status.ShouldBe(CorporateActionStatus.Applied);
        action.AppliedAt.ShouldBe(At.AddSeconds(2));
        action.EffectiveAt.ShouldBe(At);
    }

    [Test]
    public void MarkApplied_WhileAnnounced_Throws() =>
        Should.Throw<DomainException>(() => Manual().MarkApplied(At));

    [Test]
    public void NoStatusGoesBack_EveryOtherMoveThrows()
    {
        var effective = Manual();
        effective.MakeEffective(At);
        Should.Throw<DomainException>(() => effective.MakeEffective(At));
        Should.Throw<DomainException>(() => effective.Cancel(7, "No", At));
        Should.Throw<DomainException>(() => effective.Change(CorporateActionKind.Split, new CorporateActionRatio(3, 1), ExDate, null, null, null));

        var applied = Manual();
        applied.MakeEffective(At);
        applied.MarkApplied(At);
        Should.Throw<DomainException>(() => applied.MarkApplied(At));
        Should.Throw<DomainException>(() => applied.MakeEffective(At));
        Should.Throw<DomainException>(() => applied.Cancel(7, "No", At));

        var cancelled = Manual();
        cancelled.Cancel(7, "No", At);
        Should.Throw<DomainException>(() => cancelled.MakeEffective(At));
        Should.Throw<DomainException>(() => cancelled.MarkApplied(At));
        Should.Throw<DomainException>(() => cancelled.Cancel(7, "Again", At));

        var feedEffective = Feed();
        feedEffective.MakeEffective(At);
        Should.Throw<DomainException>(() => feedEffective.Amend(2, new CorporateActionRatio(3, 1), ExDate, null, null));
        Should.Throw<DomainException>(() => feedEffective.CancelFromFeed(2, At));
    }
}

/// <summary>Corporate-actions R22, R25: an entitlement leaves <c>Pending</c> once.</summary>
public class CorporateActionEntitlementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 41, 2, TimeSpan.Zero);

    [Test]
    public void Create_IsPending()
    {
        var entitlement = CorporateActionEntitlement.Create(1, 5, 9);

        (entitlement.TenantId, entitlement.CorporateActionId, entitlement.AccountId, entitlement.Status)
            .ShouldBe((1, 5, 9, EntitlementStatus.Pending));
        entitlement.QuantityBefore.ShouldBeNull();
        entitlement.TransactionId.ShouldBeNull();
        entitlement.PostedAt.ShouldBeNull();
    }

    [Test]
    public void MarkPosted_KeepsBothQuantitiesAndTheTransaction()
    {
        var entitlement = CorporateActionEntitlement.Create(1, 5, 9);

        entitlement.MarkPosted(100m, 200m, 42, At);

        entitlement.Status.ShouldBe(EntitlementStatus.Posted);
        (entitlement.QuantityBefore, entitlement.QuantityAfter, entitlement.TransactionId, entitlement.PostedAt)
            .ShouldBe(((decimal?)100m, (decimal?)200m, (int?)42, (DateTimeOffset?)At));
        entitlement.Reason.ShouldBeNull();
    }

    [TestCase(100, 100)]
    [TestCase(100, 0)]
    [TestCase(0, 10)]
    public void MarkPosted_AnUnchangedOrZeroQuantity_Throws(decimal before, decimal after) =>
        Should.Throw<DomainException>(() => CorporateActionEntitlement.Create(1, 5, 9).MarkPosted(before, after, 42, At));

    [Test]
    public void MarkNotApplied_KeepsTheReason()
    {
        var entitlement = CorporateActionEntitlement.Create(1, 5, 9);

        entitlement.MarkNotApplied(EntitlementReason.TooSmall, At);

        entitlement.Status.ShouldBe(EntitlementStatus.NotApplied);
        entitlement.Reason.ShouldBe(EntitlementReason.TooSmall);
        entitlement.PostedAt.ShouldBe(At);
        entitlement.TransactionId.ShouldBeNull();
    }

    [Test]
    public void LeavingPending_HappensOnce()
    {
        var posted = CorporateActionEntitlement.Create(1, 5, 9);
        posted.MarkPosted(100m, 200m, 42, At);
        Should.Throw<DomainException>(() => posted.MarkPosted(200m, 400m, 43, At));
        Should.Throw<DomainException>(() => posted.MarkNotApplied(EntitlementReason.NoHolding, At));

        var notApplied = CorporateActionEntitlement.Create(1, 5, 9);
        notApplied.MarkNotApplied(EntitlementReason.NoHolding, At);
        Should.Throw<DomainException>(() => notApplied.MarkPosted(100m, 200m, 42, At));
        Should.Throw<DomainException>(() => notApplied.MarkNotApplied(EntitlementReason.TooSmall, At));
    }
}
