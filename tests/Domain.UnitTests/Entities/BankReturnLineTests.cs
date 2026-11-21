using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Bank-returns §5, R3–R10: the statement line transitions of receipt returns, recalls and other debit lines.</summary>
public class BankReturnLineTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 2, 1, 7, TimeSpan.Zero);
    private const int Admin = 42;

    [Test]
    public void MarkUnknown_PutsADebitOrACreditLineInSuspense_WithTheReason()
    {
        var debit = Line(BankLineDirection.Debit);
        var credit = Line(BankLineDirection.Credit);

        debit.MarkUnknown(UnmatchedReason.UnknownPayment);
        credit.MarkUnknown(UnmatchedReason.UnknownReturn);

        debit.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        debit.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        credit.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        credit.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReturn);
    }

    [Test]
    public void MarkUnknown_OnlyFromReceived()
    {
        var line = Line(BankLineDirection.Debit);
        line.MarkUnknown(UnmatchedReason.UnknownPayment);

        Should.Throw<StatementLineStatusException>(() => line.MarkUnknown(UnmatchedReason.RecallNotCovered));
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
    }

    [Test]
    public void IgnoreAndReopen_OnAnUnmatchedDebitLine()
    {
        var debit = UnknownDebit();

        debit.Ignore("Bank fee", Admin, At);
        debit.Status.ShouldBe(BankStatementLineStatus.Ignored);
        debit.LastAction.ShouldBe(StatementLineAction.Ignored);
        debit.LastActionNote.ShouldBe("Bank fee");

        debit.Reopen(Admin, At.AddMinutes(1));
        debit.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        debit.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        debit.LastAction.ShouldBe(StatementLineAction.Reopened);
    }

    [Test]
    public void MatchRequestReturnAndUnmatch_OnADebitLine_Throw()
    {
        var actions = new Action<BankStatementLine>[]
        {
            line => line.MatchByHand(11, 12, null, Admin, At),
            line => line.RequestReturn("Note", Admin, At),
            line => line.Unmatch("Note", Admin, At),
            line => line.EnsureCanMatchByHand(),
            line => line.EnsureCanUnmatch()
        };

        foreach (var act in actions)
        {
            var debit = UnknownDebit();
            Should.Throw<StatementLineStatusException>(() => act(debit));
            debit.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        }
    }

    [Test]
    public void MarkReturnSent_FromReturnRequested()
    {
        var line = ReturnRequested();

        line.MarkReturnSent();

        line.Status.ShouldBe(BankStatementLineStatus.ReturnSent);
    }

    [Test]
    public void MarkReturnRejected_PutsTheLineBackInSuspense_WithReturnRejected()
    {
        var line = ReturnRequested();

        line.MarkReturnRejected();

        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.ReturnRejected);
    }

    [TestCase(BankStatementLineStatus.ReturnRequested)]
    [TestCase(BankStatementLineStatus.ReturnSent)]
    [TestCase(BankStatementLineStatus.Unmatched)]
    [TestCase(BankStatementLineStatus.Ignored)]
    public void MarkReturned_FromAReceiptInSuspenseOrBeingReturned(BankStatementLineStatus from)
    {
        var line = CreditIn(from);

        line.MarkReturned();

        line.Status.ShouldBe(BankStatementLineStatus.Returned);
        line.MatchedAccountId.ShouldBeNull();
    }

    [Test]
    public void MarkReturned_FromMatched_DropsTheMatch()
    {
        var line = CreditIn(BankStatementLineStatus.Matched);

        line.MarkReturned();

        line.Status.ShouldBe(BankStatementLineStatus.Returned);
        line.MatchedAccountId.ShouldBeNull();
        line.MatchedTransactionId.ShouldBeNull();
        line.MatchMethod.ShouldBeNull();
        line.MatchedAt.ShouldBeNull();
    }

    [Test]
    public void MarkRecall_WithTheReversal_MatchesTheDebitWithAccountAndTransaction()
    {
        var debit = Line(BankLineDirection.Debit);

        debit.MarkRecall(5, 11, 12, At);

        debit.Status.ShouldBe(BankStatementLineStatus.Matched);
        debit.MatchMethod.ShouldBe(StatementLineMatchMethod.Recall);
        debit.MatchedAccountId.ShouldBe(11);
        debit.MatchedTransactionId.ShouldBe(12);
        debit.MatchedAt.ShouldBe(At);
        debit.RelatedLineId.ShouldBe(5);
    }

    [Test]
    public void MarkRecall_OfAReceiptNeverCredited_MatchesWithNoAccount()
    {
        var debit = Line(BankLineDirection.Debit);

        debit.MarkRecall(5, null, null, At);

        debit.Status.ShouldBe(BankStatementLineStatus.Matched);
        debit.MatchMethod.ShouldBe(StatementLineMatchMethod.Recall);
        debit.MatchedAccountId.ShouldBeNull();
        debit.MatchedTransactionId.ShouldBeNull();
        debit.RelatedLineId.ShouldBe(5);
    }

    [Test]
    public void MarkRecall_NeedsBothAccountAndTransaction_OrNeither()
    {
        Should.Throw<DomainException>(() => Line(BankLineDirection.Debit).MarkRecall(5, 11, null, At));
        Should.Throw<DomainException>(() => Line(BankLineDirection.Debit).MarkRecall(5, null, 12, At));
    }

    [Test]
    public void MarkReturnPaid_MatchesTheDebitByPayment_WithNoAccount()
    {
        var debit = Line(BankLineDirection.Debit);

        debit.MarkReturnPaid(5, At);

        debit.Status.ShouldBe(BankStatementLineStatus.Matched);
        debit.MatchMethod.ShouldBe(StatementLineMatchMethod.Payment);
        debit.MatchedAccountId.ShouldBeNull();
        debit.MatchedTransactionId.ShouldBeNull();
        debit.MatchedAt.ShouldBe(At);
        debit.RelatedLineId.ShouldBe(5);
        debit.WithdrawalId.ShouldBeNull();
    }

    [Test]
    public void RecallAndReturnPaid_AreDebitLines_FromReceived()
    {
        Should.Throw<StatementLineStatusException>(() => Line(BankLineDirection.Credit).MarkRecall(5, null, null, At));
        Should.Throw<StatementLineStatusException>(() => Line(BankLineDirection.Credit).MarkReturnPaid(5, At));
        Should.Throw<StatementLineStatusException>(() => UnknownDebit().MarkRecall(5, null, null, At));
        Should.Throw<StatementLineStatusException>(() => UnknownDebit().MarkReturnPaid(5, At));
    }

    [Test]
    public void Reopen_OfAReturnRequestedLine_IsRefused_OnceTheReturnWasAttempted()
    {
        var line = ReturnRequested();

        Should.Throw<StatementLineStatusException>(() => line.Reopen(Admin, At, returnAttempted: true))
            .Message.ShouldBe("The bank has been asked to return this receipt, so it can no longer be reopened.");
        line.Status.ShouldBe(BankStatementLineStatus.ReturnRequested);

        line.Reopen(Admin, At, returnAttempted: false);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public void ReturnSentAndReturnedLines_AcceptNoAction()
    {
        var actions = new (string Name, Action<BankStatementLine> Act)[]
        {
            ("match", line => line.MatchByHand(11, 12, null, Admin, At)),
            ("ignore", line => line.Ignore("Note", Admin, At)),
            ("request return", line => line.RequestReturn("Note", Admin, At)),
            ("reopen", line => line.Reopen(Admin, At)),
            ("unmatch", line => line.Unmatch("Note", Admin, At)),
            ("return sent", line => line.MarkReturnSent()),
            ("return rejected", line => line.MarkReturnRejected())
        };

        foreach (var status in new[] { BankStatementLineStatus.ReturnSent, BankStatementLineStatus.Returned })
        {
            foreach (var (name, act) in actions)
            {
                var line = CreditIn(status);
                Should.Throw<StatementLineStatusException>(() => act(line), $"{name} from {status}");
                line.Status.ShouldBe(status, $"{name} from {status}");
            }
        }

        var returned = CreditIn(BankStatementLineStatus.Returned);
        Should.Throw<StatementLineStatusException>(returned.MarkReturned);
    }

    [Test]
    public void AMatchedDebitLine_AcceptsNoAction()
    {
        var recall = Line(BankLineDirection.Debit);
        recall.MarkRecall(5, 11, 12, At);
        var paid = Line(BankLineDirection.Debit);
        paid.MarkReturnPaid(5, At);

        foreach (var line in new[] { recall, paid })
        {
            Should.Throw<StatementLineStatusException>(() => line.Ignore("Note", Admin, At));
            Should.Throw<StatementLineStatusException>(() => line.Reopen(Admin, At));
            Should.Throw<StatementLineStatusException>(() => line.Unmatch("Note", Admin, At));
            line.Status.ShouldBe(BankStatementLineStatus.Matched);
        }
    }

    private static BankStatementLine Line(BankLineDirection direction) =>
        BankStatementLine.Receive(
            3, 9, "simulator", "w-L7", new DateOnly(2026, 11, 21), direction, 500.00m, "USD",
            "J SMITH", "****4200", "K7M3Q9PXB", At);

    private static BankStatementLine UnknownDebit()
    {
        var line = Line(BankLineDirection.Debit);
        line.MarkUnknown(UnmatchedReason.UnknownPayment);
        return line;
    }

    private static BankStatementLine ReturnRequested() => CreditIn(BankStatementLineStatus.ReturnRequested);

    private static BankStatementLine CreditIn(BankStatementLineStatus status)
    {
        var line = Line(BankLineDirection.Credit);
        if (status == BankStatementLineStatus.Matched)
        {
            line.MarkMatched(11, 12, At);
            return line;
        }

        line.MarkUnmatched(UnmatchedReason.NoReference);
        switch (status)
        {
            case BankStatementLineStatus.Ignored:
                line.Ignore("Note", Admin, At);
                break;
            case BankStatementLineStatus.ReturnRequested:
                line.RequestReturn("Note", Admin, At);
                break;
            case BankStatementLineStatus.ReturnSent:
                line.RequestReturn("Note", Admin, At);
                line.MarkReturnSent();
                break;
            case BankStatementLineStatus.Returned:
                line.RequestReturn("Note", Admin, At);
                line.MarkReturned();
                break;
        }

        return line;
    }
}
