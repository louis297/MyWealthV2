using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Withdrawals R29–R33, R40, R42 (docs/features/withdrawals.md §5): the outgoing payment.</summary>
public class OutgoingPaymentTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 4, 6, 0, TimeSpan.Zero);

    [Test]
    public void ForWithdrawal_IsPending_FromTheClientMoneyAccount_WithTheWithdrawalsIdAsInstruction()
    {
        var withdrawal = Withdrawal();
        var clientMoney = ClientMoney();

        var payment = OutgoingPayment.ForWithdrawal(withdrawal, clientMoney);

        payment.TenantId.ShouldBe(7);
        payment.ClientMoneyAccountId.ShouldBe(40);
        payment.Connection.ShouldBe("simulator");
        payment.InstructionPublicId.ShouldBe(withdrawal.PublicId);
        payment.WithdrawalId.ShouldBe(30);
        payment.Amount.ShouldBe(300m);
        payment.Currency.ShouldBe("USD");
        payment.Reference.ShouldBe("K7M3Q9PXB");
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.Attempts.ShouldBe(0);
        payment.PublicId.ShouldNotBe(Guid.Empty);
        payment.PublicId.ShouldNotBe(withdrawal.PublicId);
    }

    [Test]
    public void ForWithdrawal_RefusesAClientMoneyAccountOfAnotherTenantOrCurrency()
    {
        Should.Throw<DomainException>(() => OutgoingPayment.ForWithdrawal(Withdrawal(), ClientMoney(tenantId: 8)));
        Should.Throw<DomainException>(() => OutgoingPayment.ForWithdrawal(Withdrawal(), ClientMoney(currency: "NZD")));
    }

    [Test]
    public void RecordAttempt_CountsAndKeepsTheTime_AndStaysPending()
    {
        var payment = Pending();

        payment.RecordAttempt(At);
        payment.RecordAttempt(At.AddMinutes(1));

        payment.Attempts.ShouldBe(2);
        payment.LastAttemptAt.ShouldBe(At.AddMinutes(1));
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
    }

    [Test]
    public void Accept_KeepsTheBanksPaymentId_AndClearsTheLastFailure()
    {
        var payment = Pending();
        payment.RecordAttempt(At);
        payment.RecordFailure(BankFeedFailure.Timeout);

        payment.Accept("w1-P4", At.AddSeconds(1));

        payment.Status.ShouldBe(OutgoingPaymentStatus.Accepted);
        payment.BankPaymentId.ShouldBe("w1-P4");
        payment.AcceptedAt.ShouldBe(At.AddSeconds(1));
        payment.LastFailure.ShouldBeNull();
    }

    [TestCase("")]
    [TestCase(null)]
    public void Accept_WithoutAPaymentId_Throws(string? id) =>
        Should.Throw<DomainException>(() => Pending().Accept(id!, At));

    [Test]
    public void Reject_KeepsTheReason()
    {
        var payment = Pending();

        payment.Reject(PaymentRejectReason.InvalidAccount);

        payment.Status.ShouldBe(OutgoingPaymentStatus.Rejected);
        payment.RejectReason.ShouldBe(PaymentRejectReason.InvalidAccount);
    }

    [Test]
    public void RecordFailure_StaysPending_WithTheFailure()
    {
        var payment = Pending();

        payment.RecordFailure(BankFeedFailure.Unavailable);

        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.LastFailure.ShouldBe(BankFeedFailure.Unavailable);
    }

    [Test]
    public void RecordFailure_RefusesInvalidLine() =>
        Should.Throw<DomainException>(() => Pending().RecordFailure(BankFeedFailure.InvalidLine));

    [Test]
    public void Cancel_WithNoAttempt_IsCancelled()
    {
        var payment = Pending();

        payment.Cancel();

        payment.Status.ShouldBe(OutgoingPaymentStatus.Cancelled);
    }

    [Test]
    public void Cancel_AfterAnAttempt_Throws()
    {
        var payment = Pending();
        payment.RecordAttempt(At);

        Should.Throw<OutgoingPaymentAttemptedException>(payment.Cancel).Message
            .ShouldBe("The bank has been asked to pay this withdrawal, so it can no longer be cancelled.");
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
    }

    [Test]
    public void MarkPaid_FromPendingOrAccepted_IsPaid_ThenMarkReturned_IsReturned()
    {
        var lost = Pending();
        lost.MarkPaid();
        lost.Status.ShouldBe(OutgoingPaymentStatus.Paid);

        var accepted = Pending();
        accepted.Accept("w1-P1", At);
        accepted.MarkPaid();
        accepted.MarkReturned();
        accepted.Status.ShouldBe(OutgoingPaymentStatus.Returned);
    }

    [Test]
    public void AnActionFromAWrongStatus_Throws()
    {
        var accepted = Pending();
        accepted.Accept("w1-P1", At);
        Should.Throw<DomainException>(() => accepted.RecordAttempt(At));
        Should.Throw<DomainException>(() => accepted.Accept("again", At));
        Should.Throw<DomainException>(() => accepted.Reject(PaymentRejectReason.Refused));
        Should.Throw<DomainException>(accepted.Cancel);
        Should.Throw<DomainException>(accepted.MarkReturned);

        var rejected = Pending();
        rejected.Reject(PaymentRejectReason.Refused);
        Should.Throw<DomainException>(rejected.MarkPaid);

        var cancelled = Pending();
        cancelled.Cancel();
        Should.Throw<DomainException>(cancelled.MarkPaid);
        Should.Throw<DomainException>(() => cancelled.RecordAttempt(At));
    }

    [Test]
    public void ForWithdrawal_IsOfKindWithdrawal_WithNoReturnedLine()
    {
        var payment = Pending();

        payment.Kind.ShouldBe(OutgoingPaymentKind.Withdrawal);
        payment.ReturnOfLineId.ShouldBeNull();
    }

    [Test]
    public void ForReceiptReturn_IsPending_NamesTheLine_WithItsOwnInstructionId_NoWithdrawalPayeeOrReference()
    {
        var line = ReturnRequestedLine();

        var payment = OutgoingPayment.ForReceiptReturn(line, ClientMoney());
        var second = OutgoingPayment.ForReceiptReturn(line, ClientMoney());

        payment.Kind.ShouldBe(OutgoingPaymentKind.ReceiptReturn);
        payment.ReturnOfLineId.ShouldBe(50);
        payment.WithdrawalId.ShouldBeNull();
        payment.TenantId.ShouldBe(7);
        payment.ClientMoneyAccountId.ShouldBe(40);
        payment.Connection.ShouldBe("simulator");
        payment.Amount.ShouldBe(250.50m);
        payment.Currency.ShouldBe("USD");
        payment.Reference.ShouldBeNull();
        payment.Status.ShouldBe(OutgoingPaymentStatus.Pending);
        payment.Attempts.ShouldBe(0);
        payment.InstructionPublicId.ShouldBe(payment.PublicId);
        payment.InstructionPublicId.ShouldNotBe(line.PublicId);
        second.InstructionPublicId.ShouldNotBe(payment.InstructionPublicId);
    }

    [Test]
    public void ForReceiptReturn_NeedsASavedCreditLine_OfTheClientMoneyAccount()
    {
        var unsaved = ReturnRequestedLine();
        unsaved.Id = 0;
        Should.Throw<DomainException>(() => OutgoingPayment.ForReceiptReturn(unsaved, ClientMoney()));

        var otherAccount = ClientMoney();
        otherAccount.Id = 41;
        Should.Throw<DomainException>(() => OutgoingPayment.ForReceiptReturn(ReturnRequestedLine(), otherAccount));

        var debit = BankStatementLine.Receive(
            7, 40, "simulator", "w-L9", new DateOnly(2026, 11, 21), BankLineDirection.Debit, 250.50m, "USD",
            null, null, null, At);
        debit.Id = 51;
        Should.Throw<DomainException>(() => OutgoingPayment.ForReceiptReturn(debit, ClientMoney()));
    }

    [Test]
    public void AReceiptReturn_IsSentAcceptedRejectedAndCancelled_AsAWithdrawalsPayment()
    {
        var accepted = OutgoingPayment.ForReceiptReturn(ReturnRequestedLine(), ClientMoney());
        accepted.RecordAttempt(At);
        accepted.Accept("w-P1", At);
        accepted.Status.ShouldBe(OutgoingPaymentStatus.Accepted);
        accepted.MarkPaid();
        accepted.Status.ShouldBe(OutgoingPaymentStatus.Paid);

        var rejected = OutgoingPayment.ForReceiptReturn(ReturnRequestedLine(), ClientMoney());
        rejected.RecordAttempt(At);
        rejected.Reject(PaymentRejectReason.UnknownLine);
        rejected.RejectReason.ShouldBe(PaymentRejectReason.UnknownLine);

        var cancelled = OutgoingPayment.ForReceiptReturn(ReturnRequestedLine(), ClientMoney());
        cancelled.Cancel();
        cancelled.Status.ShouldBe(OutgoingPaymentStatus.Cancelled);
    }

    private static BankStatementLine ReturnRequestedLine()
    {
        var line = BankStatementLine.Receive(
            7, 40, "simulator", "w-L8", new DateOnly(2026, 11, 21), BankLineDirection.Credit, 250.50m, "USD",
            "J SMITH", "****4200", "Gift", At);
        line.Id = 50;
        line.MarkUnmatched(UnmatchedReason.NoReference);
        line.RequestReturn("Not a customer", 22, At);
        return line;
    }

    private static OutgoingPayment Pending() => OutgoingPayment.ForWithdrawal(Withdrawal(), ClientMoney());

    private static Withdrawal Withdrawal()
    {
        var withdrawal = MyWealthV2.Domain.Entities.Withdrawal.Request(7, 3, 5, 300m, "USD", "K7M3Q9PXB", null, 11, At);
        withdrawal.Id = 30;
        withdrawal.Approve(22, null, At);
        return withdrawal;
    }

    private static ClientMoneyAccount ClientMoney(int tenantId = 7, string currency = "USD")
    {
        var account = ClientMoneyAccount.Create(
            tenantId, Currency.Create(currency, currency, 2), "Simulator Bank", "Trust", "SIM-DEMO-" + currency, "simulator");
        account.Id = 40;
        return account;
    }
}
