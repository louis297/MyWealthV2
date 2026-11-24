using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R11, R13–R18 (docs/features/in-kind-transfers.md §5): the custody movement.</summary>
public class CustodyMovementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public void Receive_IsReceived_AndCutsFreeText()
    {
        var movement = CustodyMovement.Receive(3, 4, "simulator", "w-M1", CustodyMovementDirection.In, CustodyMovementKind.Transfer,
            "ACME.SIM", "usd", 9, 100m, new DateOnly(2026, 11, 24), "instr", null, null, " " + new string('n', 150) + " ",
            new string('1', 40), " ref ", At);

        movement.Status.ShouldBe(CustodyMovementStatus.Received);
        (movement.TenantId, movement.SafekeepingAccountId, movement.Connection, movement.OutsideId).ShouldBe((3, 4, "simulator", "w-M1"));
        movement.Currency.ShouldBe("USD");
        movement.InstrumentId.ShouldBe(9);
        movement.CounterpartyName!.Length.ShouldBe(140);
        movement.CounterpartyAccount!.Length.ShouldBe(34);
        movement.Reference.ShouldBe("ref");
        movement.ReceivedAt.ShouldBe(At);
        movement.PublicId.ShouldNotBe(Guid.Empty);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Receive_RefusesAQuantityNotAboveZero(decimal quantity) =>
        Should.Throw<DomainException>(() => Movement(CustodyMovementDirection.In, quantity));

    [TestCase(CustodyMovementDirection.In, 100, 100)]
    [TestCase(CustodyMovementDirection.Out, 100, -100)]
    public void SignedQuantity_IsInPlusOutMinus(CustodyMovementDirection direction, decimal quantity, decimal signed) =>
        Movement(direction, quantity).SignedQuantity.ShouldBe(signed);

    [Test]
    public void MatchToTransfer_SetsItsOwnLinks()
    {
        var movement = Movement();

        movement.MatchToTransfer(5, 6, 7);

        (movement.Status, movement.MatchMethod).ShouldBe((CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.Transfer));
        (movement.AccountId, movement.TransactionId, movement.InKindTransferId).ShouldBe(((int?)5, (int?)6, (int?)7));
        (movement.OrderFillId, movement.CorporateActionId, movement.UnmatchedReason).ShouldBe(((long?)null, (int?)null, (CustodyUnmatchedReason?)null));
    }

    [Test]
    public void MatchToFill_SetsOnlyTheFill()
    {
        var movement = Movement();

        movement.MatchToFill(88L);

        (movement.Status, movement.MatchMethod, movement.OrderFillId).ShouldBe(
            (CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.Trade, (long?)88L));
        (movement.AccountId, movement.TransactionId, movement.InKindTransferId, movement.CorporateActionId)
            .ShouldBe(((int?)null, (int?)null, (int?)null, (int?)null));
    }

    [Test]
    public void MatchToCorporateAction_SetsOnlyTheAction()
    {
        var movement = Movement();

        movement.MatchToCorporateAction(12);

        (movement.Status, movement.MatchMethod, movement.CorporateActionId).ShouldBe(
            (CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.CorporateAction, (int?)12));
        movement.OrderFillId.ShouldBeNull();
    }

    [Test]
    public void Leave_IsUnmatched_WithTheReason()
    {
        var movement = Movement();

        movement.Leave(CustodyUnmatchedReason.NoInstruction);

        (movement.Status, movement.UnmatchedReason).ShouldBe((CustodyMovementStatus.Unmatched, (CustodyUnmatchedReason?)CustodyUnmatchedReason.NoInstruction));
    }

    [Test]
    public void AMovementIsProcessedOnce()
    {
        var movement = Movement();
        movement.Leave(CustodyUnmatchedReason.NoInstruction);

        Should.Throw<CustodyMovementStatusException>(() => movement.MatchToFill(1L));
        Should.Throw<CustodyMovementStatusException>(() => movement.Leave(CustodyUnmatchedReason.UnsupportedKind));
    }

    [Test]
    public void Ignore_FromUnmatched_KeepsTheReason_AndRecordsTheAction()
    {
        var movement = Movement();
        movement.Leave(CustodyUnmatchedReason.NoInstruction);

        movement.Ignore(22, At, " Bulk delivery of the batch ");

        (movement.Status, movement.UnmatchedReason).ShouldBe((CustodyMovementStatus.Ignored, (CustodyUnmatchedReason?)CustodyUnmatchedReason.NoInstruction));
        (movement.LastAction, movement.LastActionByUserId, movement.LastActionAt, movement.LastActionNote)
            .ShouldBe(((CustodyMovementAction?)CustodyMovementAction.Ignore, (int?)22, (DateTimeOffset?)At, "Bulk delivery of the batch"));
    }

    [Test]
    public void Ignore_NeedsANote()
    {
        var movement = Movement();
        movement.Leave(CustodyUnmatchedReason.NoInstruction);

        Should.Throw<DomainException>(() => movement.Ignore(22, At, " "));
    }

    [Test]
    public void Ignore_FromMatched_Throws()
    {
        var movement = Movement();
        movement.MatchToFill(1L);

        Should.Throw<CustodyMovementStatusException>(() => movement.Ignore(22, At, "Note"))
            .Message.ShouldBe("This movement is Matched and cannot be ignored.");
    }

    [Test]
    public void Reopen_FromIgnored_IsUnmatchedAgain_WithItsReason()
    {
        var movement = Movement();
        movement.Leave(CustodyUnmatchedReason.UnknownTrade);
        movement.Ignore(22, At, "Note");

        movement.Reopen(23, At.AddHours(1), null);

        (movement.Status, movement.UnmatchedReason).ShouldBe((CustodyMovementStatus.Unmatched, (CustodyUnmatchedReason?)CustodyUnmatchedReason.UnknownTrade));
        (movement.LastAction, movement.LastActionByUserId, movement.LastActionNote).ShouldBe(((CustodyMovementAction?)CustodyMovementAction.Reopen, (int?)23, (string?)null));
    }

    [Test]
    public void Reopen_FromUnmatched_Throws()
    {
        var movement = Movement();
        movement.Leave(CustodyUnmatchedReason.UnknownTrade);

        Should.Throw<CustodyMovementStatusException>(() => movement.Reopen(23, At, null))
            .Message.ShouldBe("This movement is Unmatched and cannot be reopened.");
    }

    private static CustodyMovement Movement(CustodyMovementDirection direction = CustodyMovementDirection.In, decimal quantity = 100m) =>
        CustodyMovement.Receive(3, 4, "simulator", "w-M1", direction, CustodyMovementKind.Transfer, "ACME.SIM", "USD", 9,
            quantity, new DateOnly(2026, 11, 24), null, null, null, null, null, null, At);
}
