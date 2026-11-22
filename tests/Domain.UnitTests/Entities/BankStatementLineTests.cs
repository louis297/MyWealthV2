using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Deposit matching §5: <c>BankStatementLine</c>, R10–R12, R14, R23–R25, R31.</summary>
public class BankStatementLineTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 2, 1, 7, TimeSpan.Zero);
    private const int Admin = 42;

    [Test]
    public void Receive_KeepsThePaymentFields_AsSent_AndCutsTheReturnReasonTo140()
    {
        var line = BankStatementLine.Receive(
            3, 9, "simulator", "w-L8", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 300m, "USD",
            "J Smith", null, null, At, endToEndId: " 6b1f ", returnOfOutsideId: "w-L7", returnReason: "  " + new string('r', 150));

        line.EndToEndId.ShouldBe(" 6b1f ");
        line.ReturnOfOutsideId.ShouldBe("w-L7");
        line.ReturnReason.ShouldBe(new string('r', 140));
        line.RelatedLineId.ShouldBeNull();
        line.WithdrawalId.ShouldBeNull();
    }

    [TestCase(101, 0)]
    [TestCase(0, 101)]
    public void Receive_RefusesAnEndToEndIdOrAReturnedLineIdOver100(int endToEnd, int returnOf)
    {
        Should.Throw<DomainException>(() => BankStatementLine.Receive(
            3, 9, "simulator", "w-L8", new DateOnly(2026, 11, 20), BankLineDirection.Debit, 300m, "USD", null, null, null, At,
            endToEndId: endToEnd == 0 ? null : new string('e', endToEnd),
            returnOfOutsideId: returnOf == 0 ? null : new string('r', returnOf)));
        BankStatementLine.FitsPaymentIds(endToEnd == 0 ? null : new string('e', endToEnd), returnOf == 0 ? null : new string('r', returnOf))
            .ShouldBeFalse();
    }

    [Test]
    public void FitsPaymentIds_AtTheLimit_AndWithNone()
    {
        BankStatementLine.FitsPaymentIds(new string('e', 100), new string('r', 100)).ShouldBeTrue();
        BankStatementLine.FitsPaymentIds(null, null).ShouldBeTrue();
    }

    [Test]
    public void Receive_IsATransferByDefault_AndKeepsTheKind()
    {
        BankStatementLine.Receive(3, 9, "simulator", "w-L1", new DateOnly(2026, 12, 31), BankLineDirection.Credit, 520m, "USD",
            "Bank interest", null, null, At).Kind.ShouldBe(StatementLineKind.Transfer);
        BankStatementLine.Receive(3, 9, "simulator", "w-L2", new DateOnly(2026, 12, 31), BankLineDirection.Credit, 520m, "USD",
            "Bank interest", null, null, At, kind: StatementLineKind.Interest).Kind.ShouldBe(StatementLineKind.Interest);
    }

    [Test]
    public void SetAsideAsBankInterest_IgnoresAReceivedCreditInterestLine_WithTheNoteAndNoPerson()
    {
        var line = BankStatementLine.Receive(3, 9, "simulator", "w-L1", new DateOnly(2026, 12, 31), BankLineDirection.Credit, 520m,
            "USD", "Bank interest", null, null, At, kind: StatementLineKind.Interest);

        line.SetAsideAsBankInterest(At.AddMinutes(1));

        line.Status.ShouldBe(BankStatementLineStatus.Ignored);
        (line.LastAction, line.LastActionNote, line.LastActionByUserId, line.LastActionAt)
            .ShouldBe((StatementLineAction.Ignored, "Bank interest", (int?)null, (DateTimeOffset?)At.AddMinutes(1)));
        line.Reopen(Admin, At.AddMinutes(2));
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public void SetAsideAsBankInterest_RefusesATransfer_ADebit_AndALineNotReceived()
    {
        var transfer = BankStatementLine.Receive(3, 9, "simulator", "w-L1", new DateOnly(2026, 12, 31), BankLineDirection.Credit, 520m,
            "USD", null, null, null, At);
        var debit = BankStatementLine.Receive(3, 9, "simulator", "w-L2", new DateOnly(2026, 12, 31), BankLineDirection.Debit, 5m,
            "USD", null, null, null, At, kind: StatementLineKind.Interest);
        var done = BankStatementLine.Receive(3, 9, "simulator", "w-L3", new DateOnly(2026, 12, 31), BankLineDirection.Credit, 520m,
            "USD", null, null, null, At, kind: StatementLineKind.Interest);
        done.SetAsideAsBankInterest(At);

        Should.Throw<DomainException>(() => transfer.SetAsideAsBankInterest(At));
        Should.Throw<DomainException>(() => debit.SetAsideAsBankInterest(At));
        Should.Throw<DomainException>(() => done.SetAsideAsBankInterest(At));
        transfer.Status.ShouldBe(BankStatementLineStatus.Received);
        debit.Status.ShouldBe(BankStatementLineStatus.Received);
    }

    [Test]
    public void LinkReturnedLine_KeepsTheStoredLine()
    {
        var line = BankStatementLine.Receive(
            3, 9, "simulator", "w-L8", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 300m, "USD", null, null, null, At,
            returnOfOutsideId: "w-L7");

        line.LinkReturnedLine(77);

        line.RelatedLineId.ShouldBe(77);
    }

    [Test]
    public void Receive_IsReceived_AndKeepsTheBanksLine()
    {
        var line = BankStatementLine.Receive(
            3, 9, "simulator", "w-L7", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 500.00m, "usd",
            "  J SMITH ", "****4200", " K7M 3Q9 PXB ", At);

        line.TenantId.ShouldBe(3);
        line.ClientMoneyAccountId.ShouldBe(9);
        line.Connection.ShouldBe("simulator");
        line.OutsideId.ShouldBe("w-L7");
        line.ValueDate.ShouldBe(new DateOnly(2026, 11, 20));
        line.Direction.ShouldBe(BankLineDirection.Credit);
        line.Amount.ShouldBe(500.00m);
        line.Currency.ShouldBe("USD");
        line.PayerName.ShouldBe("J SMITH");
        line.PayerAccountMasked.ShouldBe("****4200");
        line.ReferenceText.ShouldBe("K7M 3Q9 PXB");
        line.ReceivedAt.ShouldBe(At);
        line.Status.ShouldBe(BankStatementLineStatus.Received);
        line.PublicId.ShouldNotBe(Guid.Empty);
        line.UnmatchedReason.ShouldBeNull();
        line.MatchedAccountId.ShouldBeNull();
        line.LastAction.ShouldBeNull();
        line.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Receive_CutsFreeTextTo140_AndBlankToNull()
    {
        var line = BankStatementLine.Receive(
            3, 9, "simulator", "w-L7", new DateOnly(2026, 11, 20), BankLineDirection.Credit, 1m, "USD",
            new string('P', 150), null, "   ", At);

        line.PayerName!.Length.ShouldBe(140);
        line.ReferenceText.ShouldBeNull();
        line.PayerAccountMasked.ShouldBeNull();
    }

    [TestCase("", 1)]
    [TestCase("   ", 1)]
    [TestCase(null, 1)]
    [TestCase("id", 0)]
    [TestCase("id", -1)]
    public void Receive_RefusesALineOutsideR10(string? outsideId, int amount) =>
        Should.Throw<DomainException>(() => BankStatementLine.Receive(
            3, 9, "simulator", outsideId!, new DateOnly(2026, 11, 20), BankLineDirection.Credit, amount, "USD",
            null, null, null, At));

    [Test]
    public void Receive_RefusesAnOutsideIdOver100_AndAnAmountWithFiveDecimals()
    {
        Should.Throw<DomainException>(() => Credit(outsideId: new string('i', 101)));
        Should.Throw<DomainException>(() => Credit(amount: 1.00001m));
        Credit(outsideId: new string('i', 100), amount: 1.0001m).Amount.ShouldBe(1.0001m);
    }

    [TestCase("id", 1, true)]
    [TestCase("id", 0.0001, true)]
    [TestCase("id", 0.00001, false)]
    [TestCase("id", 0, false)]
    [TestCase("", 1, false)]
    public void IsStorable_FollowsR10(string outsideId, decimal amount, bool expected) =>
        BankStatementLine.IsStorable(outsideId, BankLineDirection.Credit, amount).ShouldBe(expected);

    [Test]
    public void IsStorable_RefusesAnUnknownDirection() =>
        BankStatementLine.IsStorable("id", (BankLineDirection)2, 1m).ShouldBeFalse();

    [Test]
    public void MarkMatched_FromReceived_SetsTheMatchByReference_WithoutALastAction()
    {
        var line = Credit();

        line.MarkMatched(11, 12, At);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchedAccountId.ShouldBe(11);
        line.MatchedTransactionId.ShouldBe(12);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Reference);
        line.MatchedAt.ShouldBe(At);
        line.UnmatchedReason.ShouldBeNull();
        line.LastAction.ShouldBeNull();
    }

    [Test]
    public void MarkUnmatched_FromReceived_KeepsTheReason()
    {
        var line = Credit();

        line.MarkUnmatched(UnmatchedReason.UnknownReference);

        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReference);
        line.MatchedAccountId.ShouldBeNull();
    }

    [Test]
    public void MatchByHand_FromUnmatched_SetsTheManualMatchAndTheLastAction()
    {
        var line = Unmatched();

        line.MatchByHand(11, 12, "  Confirmed by phone ", Admin, At);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchedAccountId.ShouldBe(11);
        line.MatchedTransactionId.ShouldBe(12);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Manual);
        line.MatchedAt.ShouldBe(At);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.NoReference);
        line.LastAction.ShouldBe(StatementLineAction.Matched);
        line.LastActionNote.ShouldBe("Confirmed by phone");
        line.LastActionByUserId.ShouldBe(Admin);
        line.LastActionAt.ShouldBe(At);
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void MatchByHand_TakesNoNote(string? note)
    {
        var line = Unmatched();

        line.MatchByHand(11, 12, note, Admin, At);

        line.LastActionNote.ShouldBeNull();
    }

    [Test]
    public void Ignore_FromUnmatched_RecordsTheNote()
    {
        var line = Unmatched();

        line.Ignore("Bank interest", Admin, At);

        line.Status.ShouldBe(BankStatementLineStatus.Ignored);
        line.LastAction.ShouldBe(StatementLineAction.Ignored);
        line.LastActionNote.ShouldBe("Bank interest");
        line.MatchedAccountId.ShouldBeNull();
    }

    [Test]
    public void RequestReturn_FromUnmatched_RecordsTheNote()
    {
        var line = Unmatched();

        line.RequestReturn("Not our customer", Admin, At);

        line.Status.ShouldBe(BankStatementLineStatus.ReturnRequested);
        line.LastAction.ShouldBe(StatementLineAction.ReturnRequested);
        line.LastActionNote.ShouldBe("Not our customer");
    }

    [Test]
    public void Reopen_FromIgnoredAndFromReturnRequested_IsUnmatched_WithoutANote()
    {
        var ignored = Unmatched();
        ignored.Ignore("Bank interest", Admin, At);
        var returned = Unmatched();
        returned.RequestReturn("Not ours", Admin, At);

        ignored.Reopen(Admin, At.AddMinutes(1));
        returned.Reopen(Admin, At.AddMinutes(1));

        foreach (var line in new[] { ignored, returned })
        {
            line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
            line.UnmatchedReason.ShouldBe(UnmatchedReason.NoReference);
            line.LastAction.ShouldBe(StatementLineAction.Reopened);
            line.LastActionNote.ShouldBeNull();
            line.LastActionAt.ShouldBe(At.AddMinutes(1));
        }
    }

    [Test]
    public void Unmatch_FromMatched_ClearsTheMatch_AndKeepsTheReasonFromProcessing()
    {
        var byReference = Credit();
        byReference.MarkMatched(11, 12, At);
        var byHand = Unmatched();
        byHand.MatchByHand(11, 12, null, Admin, At);

        byReference.Unmatch("Wrong customer", Admin, At.AddMinutes(1));
        byHand.Unmatch("Wrong customer", Admin, At.AddMinutes(1));

        foreach (var line in new[] { byReference, byHand })
        {
            line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
            line.MatchedAccountId.ShouldBeNull();
            line.MatchedTransactionId.ShouldBeNull();
            line.MatchMethod.ShouldBeNull();
            line.MatchedAt.ShouldBeNull();
            line.LastAction.ShouldBe(StatementLineAction.Unmatched);
            line.LastActionNote.ShouldBe("Wrong customer");
        }

        byReference.UnmatchedReason.ShouldBeNull();
        byHand.UnmatchedReason.ShouldBe(UnmatchedReason.NoReference);
    }

    [Test]
    public void EveryAction_FromAStatusNotInR23_Throws_AndChangesNothing()
    {
        var actions = new (string Name, Action<BankStatementLine> Act, BankStatementLineStatus[] From)[]
        {
            ("match", line => line.MatchByHand(11, 12, null, Admin, At), [BankStatementLineStatus.Unmatched]),
            ("ignore", line => line.Ignore("Note", Admin, At), [BankStatementLineStatus.Unmatched]),
            ("request return", line => line.RequestReturn("Note", Admin, At), [BankStatementLineStatus.Unmatched]),
            ("reopen", line => line.Reopen(Admin, At),
                [BankStatementLineStatus.Ignored, BankStatementLineStatus.ReturnRequested]),
            ("unmatch", line => line.Unmatch("Note", Admin, At), [BankStatementLineStatus.Matched]),
            ("mark matched", line => line.MarkMatched(11, 12, At), [BankStatementLineStatus.Received]),
            ("mark unmatched", line => line.MarkUnmatched(UnmatchedReason.NoReference), [BankStatementLineStatus.Received])
        };

        foreach (var (name, act, from) in actions)
        {
            foreach (var status in Enum.GetValues<BankStatementLineStatus>().Except(from))
            {
                var line = InStatus(status);
                var before = (line.Status, line.MatchedAccountId, line.LastAction);

                Should.Throw<StatementLineStatusException>(() => act(line), $"{name} from {status}");

                (line.Status, line.MatchedAccountId, line.LastAction).ShouldBe(before, $"{name} from {status}");
            }
        }
    }

    [Test]
    public void WrongStatus_SaysWhatTheReceiptIs()
    {
        var line = InStatus(BankStatementLineStatus.Matched);

        Should.Throw<StatementLineStatusException>(() => line.Ignore("Note", Admin, At))
            .Message.ShouldBe("This receipt is Matched and cannot be ignored.");
    }

    [Test]
    public void EveryReceiptMethod_OnADebitLine_Throws()
    {
        // Bank-returns R9: ignore and reopen apply to a debit line too (BankReturnLineTests).
        var actions = new Action<BankStatementLine>[]
        {
            line => line.MarkMatched(11, 12, At),
            line => line.MarkUnmatched(UnmatchedReason.NoReference),
            line => line.MatchByHand(11, 12, null, Admin, At),
            line => line.RequestReturn("Note", Admin, At),
            line => line.Unmatch("Note", Admin, At),
            line => line.MarkReturnSent(),
            line => line.MarkReturnRejected(),
            line => line.MarkReturned()
        };

        foreach (var act in actions)
        {
            var debit = Credit(direction: BankLineDirection.Debit);
            Should.Throw<StatementLineStatusException>(() => act(debit));
            debit.Status.ShouldBe(BankStatementLineStatus.Received);
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void NoteRequired_OnIgnoreRequestReturnAndUnmatch(string? note)
    {
        Should.Throw<DomainException>(() => Unmatched().Ignore(note!, Admin, At));
        Should.Throw<DomainException>(() => Unmatched().RequestReturn(note!, Admin, At));
        var matched = InStatus(BankStatementLineStatus.Matched);
        Should.Throw<DomainException>(() => matched.Unmatch(note!, Admin, At));
        matched.Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public void Note_IsAtMost200Characters()
    {
        var note200 = new string('n', 200);
        var note201 = new string('n', 201);

        Should.Throw<DomainException>(() => Unmatched().Ignore(note201, Admin, At));
        Should.Throw<DomainException>(() => Unmatched().MatchByHand(11, 12, note201, Admin, At));
        var line = Unmatched();
        line.Ignore(note200, Admin, At);
        line.LastActionNote.ShouldBe(note200);
    }

    private static BankStatementLine Credit(
        string outsideId = "w-L7",
        decimal amount = 500.00m,
        BankLineDirection direction = BankLineDirection.Credit) =>
        BankStatementLine.Receive(
            3, 9, "simulator", outsideId, new DateOnly(2026, 11, 20), direction, amount, "USD",
            "J SMITH", "****4200", "K7M3Q9PXB", At);

    private static BankStatementLine Unmatched()
    {
        var line = Credit();
        line.MarkUnmatched(UnmatchedReason.NoReference);
        return line;
    }

    private static BankStatementLine InStatus(BankStatementLineStatus status)
    {
        var line = Credit();
        switch (status)
        {
            case BankStatementLineStatus.Received:
                break;
            case BankStatementLineStatus.Matched:
                line.MarkMatched(11, 12, At);
                break;
            case BankStatementLineStatus.Unmatched:
                line.MarkUnmatched(UnmatchedReason.NoReference);
                break;
            case BankStatementLineStatus.Ignored:
                line.MarkUnmatched(UnmatchedReason.NoReference);
                line.Ignore("Note", Admin, At);
                break;
            case BankStatementLineStatus.ReturnRequested:
                line.MarkUnmatched(UnmatchedReason.NoReference);
                line.RequestReturn("Note", Admin, At);
                break;
            case BankStatementLineStatus.ReturnSent:
                line.MarkUnmatched(UnmatchedReason.NoReference);
                line.RequestReturn("Note", Admin, At);
                line.MarkReturnSent();
                break;
            case BankStatementLineStatus.Returned:
                line.MarkUnmatched(UnmatchedReason.NoReference);
                line.MarkReturned();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return line;
    }
}
