using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Rebalances;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Rebalances;

/// <summary>Rebalances R3, R9, R11, R13, R14, R17, R19 (docs/features/rebalances.md §5): the rebalance and its
/// moves.</summary>
public class RebalanceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 23, 2, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);
    private const int Adviser = 7;

    private static RebalanceSubject Subject(
        RebalanceTrigger trigger = RebalanceTrigger.Account, int? by = Adviser, Guid? publicId = null) =>
        new(1, 10, 20, 30, trigger, "usd", 100m, 1m,
            [new RebalanceTargetSpec(101, 60m), new RebalanceTargetSpec(102, 40m)], by, publicId);

    private static RebalanceLineSpec Buy(int instrumentId, decimal quantity, decimal price, decimal targetWeight) =>
        new(instrumentId, 0m, 0m, targetWeight, "USD",
            new RebalanceLineTrade(OrderSide.Buy, quantity, price, Day, quantity * price, quantity * price * 0.005m));

    private static RebalanceLineSpec Sell(int instrumentId, decimal quantity, decimal price) =>
        new(instrumentId, quantity, 40m, null, "USD",
            new RebalanceLineTrade(OrderSide.Sell, quantity, price, Day, quantity * price, 5m));

    private static Rebalance Proposed(params RebalanceLineSpec[] lines) =>
        Rebalance.Propose(Subject(), 10000m, 10000m,
            lines.Length == 0 ? [Buy(101, 59.4m, 100m, 60m), Buy(102, 79.2m, 50m, 40m)] : lines, At);

    private static Rebalance Confirmed(params RebalanceLineSpec[] lines)
    {
        var rebalance = Proposed(lines);
        rebalance.Confirm(Adviser, "simulator", At.AddMinutes(5));
        return rebalance;
    }

    private static Rebalance Selling()
    {
        var rebalance = Confirmed(Sell(103, 40m, 25m), Buy(101, 59.4m, 100m, 60m));
        rebalance.StartWave(OrderSide.Sell);
        return rebalance;
    }

    private static Rebalance Buying()
    {
        var rebalance = Confirmed();
        rebalance.StartWave(OrderSide.Buy);
        return rebalance;
    }

    [Test]
    public void Propose_StoresAProposal_WithItsOwnTargets_ItsLines_AndExpiresIn24Hours()
    {
        var id = Guid.NewGuid();
        var rebalance = Rebalance.Propose(Subject(publicId: id), 10000m, 9000m,
            [Buy(101, 59.4m, 100m, 60m), new RebalanceLineSpec(102, 3m, 1.5m, 40m, "USD", null)], At);

        rebalance.PublicId.ShouldBe(id);
        rebalance.Status.ShouldBe(RebalanceStatus.Proposed);
        rebalance.IsOpen.ShouldBeTrue();
        rebalance.IsInProgress.ShouldBeFalse();
        rebalance.Trigger.ShouldBe(RebalanceTrigger.Account);
        rebalance.BaseCurrency.ShouldBe("USD");
        rebalance.TotalValue.ShouldBe(10000m);
        rebalance.Cash.ShouldBe(9000m);
        rebalance.MinimumTrade.ShouldBe(100m);
        rebalance.CashResidualPercent.ShouldBe(1m);
        rebalance.ProposedByUserId.ShouldBe(Adviser);
        rebalance.ProposedAt.ShouldBe(At);
        rebalance.ExpiresAt.ShouldBe(At.AddHours(RebalanceRules.ProposalValidHours));
        rebalance.Waves.ShouldBe(0);
        rebalance.EndedAt.ShouldBeNull();
        rebalance.Targets.Select(target => (target.InstrumentId, target.TargetWeight, target.SortOrder))
            .ShouldBe([(101, 60m, 0), (102, 40m, 1)]);
        rebalance.Lines.Count.ShouldBe(2);
        var buy = rebalance.Lines[0];
        buy.Side.ShouldBe(OrderSide.Buy);
        buy.TradeQuantity.ShouldBe(59.4m);
        buy.ReferencePrice.ShouldBe(100m);
        buy.ReferencePriceDate.ShouldBe(Day);
        buy.EstimatedGross.ShouldBe(5940m);
        buy.EstimatedFee.ShouldBe(29.7m);
        var held = rebalance.Lines[1];
        held.Quantity.ShouldBe(3m);
        held.Weight.ShouldBe(1.5m);
        held.Side.ShouldBeNull();
        held.TradeQuantity.ShouldBeNull();
        held.EstimatedFee.ShouldBeNull();
    }

    [Test]
    public void Propose_WithoutAPublicId_MakesOne() =>
        Proposed().PublicId.ShouldNotBe(Guid.Empty);

    [Test]
    public void Propose_NeedsATrade() =>
        Should.Throw<DomainException>(() => Rebalance.Propose(Subject(), 10000m, 10000m,
            [new RebalanceLineSpec(101, 1m, 1m, 60m, "USD", null)], At));

    [Test]
    public void Propose_NeedsTargets() =>
        Should.Throw<DomainException>(() => Rebalance.Propose(Subject() with { Targets = [] }, 10000m, 10000m,
            [Buy(101, 1m, 1m, 60m)], At));

    [Test]
    public void NoChange_IsFinal_WithNoLines()
    {
        var rebalance = Rebalance.NoChange(Subject(RebalanceTrigger.Portfolio, null), 10000m, 50m, At);

        rebalance.Status.ShouldBe(RebalanceStatus.NoChange);
        rebalance.IsOpen.ShouldBeFalse();
        rebalance.Lines.ShouldBeEmpty();
        rebalance.TotalValue.ShouldBe(10000m);
        rebalance.ProposedByUserId.ShouldBeNull();
        rebalance.EndedAt.ShouldBe(At);
    }

    [Test]
    public void Fail_IsFinal_WithItsCodeAndMessage_AndNoFigures()
    {
        var rebalance = Rebalance.Fail(Subject(), "missing_price", "Missing price for ACME.SIM.", At);

        rebalance.Status.ShouldBe(RebalanceStatus.Failed);
        rebalance.FailureCode.ShouldBe("missing_price");
        rebalance.FailureMessage.ShouldBe("Missing price for ACME.SIM.");
        rebalance.TotalValue.ShouldBeNull();
        rebalance.Cash.ShouldBeNull();
        rebalance.Lines.ShouldBeEmpty();
        rebalance.EndedAt.ShouldBe(At);
    }

    [Test]
    public void Fail_KeepsAtMost500CharactersOfItsMessage() =>
        Rebalance.Fail(Subject(), "missing_fx", new string('x', 600), At).FailureMessage!.Length.ShouldBe(500);

    [Test]
    public void Confirm_RecordsThePerson_TheTime_AndTheConnection_AndKeepsItProposed()
    {
        var rebalance = Proposed();

        rebalance.Confirm(Adviser, "simulator", At.AddHours(1));

        rebalance.Status.ShouldBe(RebalanceStatus.Proposed);
        rebalance.IsConfirmed.ShouldBeTrue();
        rebalance.ConfirmedByUserId.ShouldBe(Adviser);
        rebalance.ConfirmedAt.ShouldBe(At.AddHours(1));
        rebalance.Connection.ShouldBe("simulator");
    }

    [Test]
    public void Confirm_AtOrAfterExpiresAt_Throws()
    {
        var rebalance = Proposed();

        Should.Throw<RebalanceStatusException>(() => rebalance.Confirm(Adviser, "simulator", rebalance.ExpiresAt))
            .Message.ShouldBe("This proposal has expired. Propose the rebalance again.");
        rebalance.IsExpired(rebalance.ExpiresAt.AddTicks(-1)).ShouldBeFalse();
        rebalance.IsExpired(rebalance.ExpiresAt).ShouldBeTrue();
    }

    [Test]
    public void Confirm_Twice_Throws() =>
        Should.Throw<RebalanceStatusException>(() => Confirmed().Confirm(Adviser, "simulator", At.AddMinutes(6)));

    [Test]
    public void Confirm_OfAFinalRebalance_Throws() =>
        Should.Throw<RebalanceStatusException>(() =>
                Rebalance.NoChange(Subject(), 1m, 1m, At).Confirm(Adviser, "simulator", At))
            .Message.ShouldBe("This rebalance is NoChange and cannot be confirmed.");

    [Test]
    public void StartWave_Sell_MovesAConfirmedProposalToSelling()
    {
        var rebalance = Selling();

        rebalance.Status.ShouldBe(RebalanceStatus.Selling);
        rebalance.IsInProgress.ShouldBeTrue();
        rebalance.Waves.ShouldBe(1);
        rebalance.BuyWaves.ShouldBe(0);
    }

    [Test]
    public void StartWave_BeforeConfirm_Throws() =>
        Should.Throw<RebalanceStatusException>(() => Proposed().StartWave(OrderSide.Buy));

    [Test]
    public void StartWave_Sell_OnlyAsTheFirstWave()
    {
        Should.Throw<RebalanceStatusException>(() => Selling().StartWave(OrderSide.Sell));
        Should.Throw<RebalanceStatusException>(() => Buying().StartWave(OrderSide.Sell));
    }

    [Test]
    public void StartWave_Buy_FromAConfirmedProposal_TheSells_OrTheFirstBuys()
    {
        var fromProposal = Buying();
        fromProposal.Status.ShouldBe(RebalanceStatus.Buying);
        fromProposal.Waves.ShouldBe(1);
        fromProposal.BuyWaves.ShouldBe(1);

        var afterSells = Selling();
        afterSells.StartWave(OrderSide.Buy);
        afterSells.Status.ShouldBe(RebalanceStatus.Buying);
        afterSells.Waves.ShouldBe(2);
        afterSells.BuyWaves.ShouldBe(1);

        afterSells.StartWave(OrderSide.Buy);
        afterSells.Waves.ShouldBe(3);
        afterSells.BuyWaves.ShouldBe(2);
    }

    [Test]
    public void StartWave_AThirdBuyWave_Throws()
    {
        var rebalance = Buying();
        rebalance.StartWave(OrderSide.Buy);

        Should.Throw<RebalanceStatusException>(() => rebalance.StartWave(OrderSide.Buy));
        rebalance.BuyWaves.ShouldBe(RebalanceRules.MaxBuyWaves);
    }

    [Test]
    public void StartWave_AfterACancelRequest_Throws()
    {
        var rebalance = Selling();
        rebalance.RequestCancel(Adviser, null, At.AddHours(1));

        Should.Throw<RebalanceStatusException>(() => rebalance.StartWave(OrderSide.Buy));
    }

    [Test]
    public void EndNoChange_EndsAProposal()
    {
        var rebalance = Confirmed();

        rebalance.EndNoChange(At.AddHours(1));

        rebalance.Status.ShouldBe(RebalanceStatus.NoChange);
        rebalance.EndedAt.ShouldBe(At.AddHours(1));
        Should.Throw<RebalanceStatusException>(() => Selling().EndNoChange(At));
    }

    [Test]
    public void Complete_EndsARebalanceInProgress()
    {
        var selling = Selling();
        selling.Complete(At.AddHours(2));
        selling.Status.ShouldBe(RebalanceStatus.Completed);
        selling.EndedAt.ShouldBe(At.AddHours(2));
        selling.IsOpen.ShouldBeFalse();

        var buying = Buying();
        buying.Complete(At.AddHours(2));
        buying.Status.ShouldBe(RebalanceStatus.Completed);
    }

    [Test]
    public void Complete_OfAProposal_Throws() =>
        Should.Throw<RebalanceStatusException>(() => Confirmed().Complete(At));

    [Test]
    public void EndIncomplete_RecordsTheCodeAndMessage()
    {
        var rebalance = Selling();

        rebalance.EndIncomplete("order_not_filled", "1 of 1 orders were not filled in full.", At.AddHours(3));

        rebalance.Status.ShouldBe(RebalanceStatus.Incomplete);
        rebalance.FailureCode.ShouldBe("order_not_filled");
        rebalance.FailureMessage.ShouldBe("1 of 1 orders were not filled in full.");
        rebalance.EndedAt.ShouldBe(At.AddHours(3));
    }

    [Test]
    public void EndIncomplete_OfAProposal_Throws() =>
        Should.Throw<RebalanceStatusException>(() => Proposed().EndIncomplete("stale_price", "Old.", At));

    [Test]
    public void RequestCancel_RecordsTheRequest_AndKeepsTheStatus()
    {
        var rebalance = Buying();

        rebalance.RequestCancel(Adviser, "  Customer is withdrawing next week  ", At.AddHours(1));

        rebalance.Status.ShouldBe(RebalanceStatus.Buying);
        rebalance.HasCancelRequest.ShouldBeTrue();
        rebalance.CancelRequestedAt.ShouldBe(At.AddHours(1));
        rebalance.CancelRequestedByUserId.ShouldBe(Adviser);
        rebalance.CancelNote.ShouldBe("Customer is withdrawing next week");
    }

    [Test]
    public void RequestCancel_ByThePlatform_HasNoPerson()
    {
        var rebalance = Selling();

        rebalance.RequestCancel(null, null, At.AddHours(1));

        rebalance.CancelRequestedByUserId.ShouldBeNull();
        rebalance.CancelNote.ShouldBeNull();
    }

    [Test]
    public void RequestCancel_Twice_Throws()
    {
        var rebalance = Selling();
        rebalance.RequestCancel(Adviser, null, At);

        Should.Throw<RebalanceStatusException>(() => rebalance.RequestCancel(Adviser, null, At))
            .Message.ShouldBe("A cancellation was already requested for this rebalance.");
    }

    [Test]
    public void RequestCancel_OfAProposalOrAFinalRebalance_Throws()
    {
        Should.Throw<RebalanceStatusException>(() => Proposed().RequestCancel(Adviser, null, At));
        var completed = Selling();
        completed.Complete(At);
        Should.Throw<RebalanceStatusException>(() => completed.RequestCancel(Adviser, null, At));
    }

    [Test]
    public void RequestCancel_NeedsANoteOf1To200() =>
        Should.Throw<DomainException>(() => Selling().RequestCancel(Adviser, new string('x', 201), At));

    [Test]
    public void Cancel_OfAProposal_IsAtOnce_WithTheReason()
    {
        var rebalance = Proposed();

        rebalance.Cancel(RebalanceCancelReason.Superseded, At.AddHours(1), anyOrderLive: false);

        rebalance.Status.ShouldBe(RebalanceStatus.Cancelled);
        rebalance.CancelReason.ShouldBe(RebalanceCancelReason.Superseded);
        rebalance.EndedAt.ShouldBe(At.AddHours(1));
        rebalance.HasCancelRequest.ShouldBeFalse();
    }

    [Test]
    public void Cancel_OfAProposal_ByAPerson_KeepsTheNote()
    {
        var rebalance = Proposed();

        rebalance.Cancel(RebalanceCancelReason.Requested, At, false, Adviser, "Not now");

        rebalance.CancelRequestedByUserId.ShouldBe(Adviser);
        rebalance.CancelNote.ShouldBe("Not now");
    }

    [Test]
    public void Cancel_InProgress_NeedsNoLiveOrder()
    {
        var rebalance = Selling();
        rebalance.RequestCancel(null, null, At);

        Should.Throw<RebalanceStatusException>(() =>
            rebalance.Cancel(RebalanceCancelReason.Unassigned, At, anyOrderLive: true));
        rebalance.Status.ShouldBe(RebalanceStatus.Selling);

        rebalance.Cancel(RebalanceCancelReason.Unassigned, At.AddHours(1), anyOrderLive: false);
        rebalance.Status.ShouldBe(RebalanceStatus.Cancelled);
        rebalance.CancelReason.ShouldBe(RebalanceCancelReason.Unassigned);
    }

    [Test]
    public void Cancel_OfAFinalRebalance_Throws()
    {
        var rebalance = Rebalance.Fail(Subject(), "missing_fx", "No rate.", At);

        Should.Throw<RebalanceStatusException>(() =>
                rebalance.Cancel(RebalanceCancelReason.Requested, At, false))
            .Message.ShouldBe("This rebalance is Failed and cannot be cancelled.");
    }

    [Test]
    public void MarkFollowUp_OnlyInProgress()
    {
        var rebalance = Buying();
        rebalance.MarkFollowUp();
        rebalance.FollowUp.ShouldBeTrue();

        Should.Throw<RebalanceStatusException>(() => Proposed().MarkFollowUp());
    }

    [TestCase(RebalanceStatus.Proposed, true, false)]
    [TestCase(RebalanceStatus.Selling, true, true)]
    [TestCase(RebalanceStatus.Buying, true, true)]
    [TestCase(RebalanceStatus.Completed, false, false)]
    [TestCase(RebalanceStatus.Incomplete, false, false)]
    [TestCase(RebalanceStatus.Cancelled, false, false)]
    [TestCase(RebalanceStatus.NoChange, false, false)]
    [TestCase(RebalanceStatus.Failed, false, false)]
    public void OpenAndInProgress_ByStatus(RebalanceStatus status, bool open, bool inProgress)
    {
        Rebalance.IsOpenStatus(status).ShouldBe(open);
        Rebalance.IsInProgressStatus(status).ShouldBe(inProgress);
    }

    [Test]
    public void Rules_AreTheSpecsNumbers()
    {
        RebalanceRules.ProposalValidHours.ShouldBe(24);
        RebalanceRules.MaxBuyWaves.ShouldBe(2);
    }
}
