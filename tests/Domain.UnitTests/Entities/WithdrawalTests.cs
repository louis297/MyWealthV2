using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Withdrawals R18–R30, R33, R40, R42 (docs/features/withdrawals.md §5): the withdrawal aggregate.</summary>
public class WithdrawalTests
{
    private const int Requester = 11;
    private const int Approver = 22;
    private static readonly DateTimeOffset At = new(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public void Request_IsRequested_Open_AndKeepsTrimmedFields()
    {
        var publicId = Guid.NewGuid();

        var withdrawal = Withdrawal.Request(7, 3, 5, 300.00m, " usd ", " K7M3Q9PXB ", "  Asked by phone ", Requester, At, publicId);

        withdrawal.TenantId.ShouldBe(7);
        withdrawal.AccountId.ShouldBe(3);
        withdrawal.NominatedBankAccountId.ShouldBe(5);
        withdrawal.Amount.ShouldBe(300.00m);
        withdrawal.Currency.ShouldBe("USD");
        withdrawal.Reference.ShouldBe("K7M3Q9PXB");
        withdrawal.Memo.ShouldBe("Asked by phone");
        withdrawal.Status.ShouldBe(WithdrawalStatus.Requested);
        withdrawal.RequestedByUserId.ShouldBe(Requester);
        withdrawal.RequestedAt.ShouldBe(At);
        withdrawal.PublicId.ShouldBe(publicId);
        withdrawal.DecisionAction.ShouldBeNull();
        withdrawal.IsOpen.ShouldBeTrue();
        withdrawal.DomainEvents.ShouldBeEmpty();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Request_RefusesAnAmountNotAboveZero(decimal amount) =>
        Should.Throw<DomainException>(() => Withdrawal.Request(1, 1, 1, amount, "USD", "REF", null, Requester, At));

    [TestCase("")]
    [TestCase("ABCDEFGHIJKLMNOPQRS")]
    [TestCase("REF/1")]
    public void Request_RefusesABadReference(string reference) =>
        Should.Throw<DomainException>(() => Withdrawal.Request(1, 1, 1, 1m, "USD", reference, null, Requester, At));

    [TestCase("ABCDEFGHIJKLMNOPQR")]
    [TestCase("Rent 2026-11.a")]
    public void Request_AcceptsAReferenceOfLettersDigitsSpacesHyphensAndFullStops(string reference) =>
        Withdrawal.Request(1, 1, 1, 1m, "USD", reference, null, Requester, At).Reference.ShouldBe(reference);

    [Test]
    public void Request_RefusesAMemoOver200() =>
        Should.Throw<DomainException>(() => Withdrawal.Request(1, 1, 1, 1m, "USD", "REF", new string('m', 201), Requester, At));

    [Test]
    public void Approve_ByAnotherPerson_IsApproved_AndRecordsTheDecision()
    {
        var withdrawal = Requested();

        withdrawal.Approve(Approver, null, At.AddMinutes(5));

        withdrawal.Status.ShouldBe(WithdrawalStatus.Approved);
        withdrawal.DecisionAction.ShouldBe(WithdrawalDecision.Approved);
        withdrawal.DecisionByUserId.ShouldBe(Approver);
        withdrawal.DecisionAt.ShouldBe(At.AddMinutes(5));
        withdrawal.DecisionNote.ShouldBeNull();
        withdrawal.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void Reject_ByAnotherPerson_WithANote_IsRejected_AndNotOpen()
    {
        var withdrawal = Requested();

        withdrawal.Reject(Approver, " Customer has not confirmed ", At);

        withdrawal.Status.ShouldBe(WithdrawalStatus.Rejected);
        withdrawal.DecisionAction.ShouldBe(WithdrawalDecision.Rejected);
        withdrawal.DecisionNote.ShouldBe("Customer has not confirmed");
        withdrawal.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void Reject_WithoutANote_Throws() =>
        Should.Throw<DomainException>(() => Requested().Reject(Approver, "  ", At));

    [Test]
    public void ApproveOrReject_ByTheRequester_Throws()
    {
        var withdrawal = Requested();

        Should.Throw<WithdrawalStatusException>(() => withdrawal.Approve(Requester, null, At)).Message
            .ShouldBe("A withdrawal must be approved by someone other than the person who requested it.");
        Should.Throw<WithdrawalStatusException>(() => withdrawal.Reject(Requester, "No", At)).Message
            .ShouldBe("A withdrawal must be approved by someone other than the person who requested it.");
        withdrawal.Status.ShouldBe(WithdrawalStatus.Requested);
    }

    [TestCase(WithdrawalStatus.Requested)]
    [TestCase(WithdrawalStatus.Approved)]
    public void Cancel_FromRequestedOrApproved_IsCancelled_ByAnyone(WithdrawalStatus from)
    {
        var withdrawal = InStatus(from);

        withdrawal.Cancel(Requester, " Changed mind ", At.AddHours(1));

        withdrawal.Status.ShouldBe(WithdrawalStatus.Cancelled);
        withdrawal.DecisionAction.ShouldBe(WithdrawalDecision.Cancelled);
        withdrawal.DecisionByUserId.ShouldBe(Requester);
        withdrawal.DecisionNote.ShouldBe("Changed mind");
        withdrawal.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void MarkSent_FromApproved_IsSent_AndStillOpen()
    {
        var withdrawal = InStatus(WithdrawalStatus.Approved);

        withdrawal.MarkSent();

        withdrawal.Status.ShouldBe(WithdrawalStatus.Sent);
        withdrawal.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void MarkFailed_FromApproved_IsFailed_WithTheReason()
    {
        var withdrawal = InStatus(WithdrawalStatus.Approved);

        withdrawal.MarkFailed(PaymentRejectReason.AccountClosed);

        withdrawal.Status.ShouldBe(WithdrawalStatus.Failed);
        withdrawal.FailureReason.ShouldBe(PaymentRejectReason.AccountClosed);
        withdrawal.IsOpen.ShouldBeFalse();
    }

    [TestCase(WithdrawalStatus.Approved)]
    [TestCase(WithdrawalStatus.Sent)]
    public void MarkPaid_FromApprovedOrSent_IsPaid_WithThePayout(WithdrawalStatus from)
    {
        var withdrawal = InStatus(from);

        withdrawal.MarkPaid(99);

        withdrawal.Status.ShouldBe(WithdrawalStatus.Paid);
        withdrawal.PayoutTransactionId.ShouldBe(99);
        withdrawal.IsOpen.ShouldBeFalse();
    }

    [Test]
    public void MarkReturned_FromPaid_IsReturned_WithTheReversalAndAReasonCutTo140()
    {
        var withdrawal = InStatus(WithdrawalStatus.Paid);

        withdrawal.MarkReturned(100, new string('r', 150));

        withdrawal.Status.ShouldBe(WithdrawalStatus.Returned);
        withdrawal.ReturnTransactionId.ShouldBe(100);
        withdrawal.PayoutTransactionId.ShouldBe(99);
        withdrawal.ReturnReason!.Length.ShouldBe(140);
        withdrawal.IsOpen.ShouldBeFalse();
    }

    [TestCase(WithdrawalStatus.Requested, true)]
    [TestCase(WithdrawalStatus.Approved, true)]
    [TestCase(WithdrawalStatus.Sent, true)]
    [TestCase(WithdrawalStatus.Paid, false)]
    [TestCase(WithdrawalStatus.Rejected, false)]
    [TestCase(WithdrawalStatus.Cancelled, false)]
    [TestCase(WithdrawalStatus.Failed, false)]
    [TestCase(WithdrawalStatus.Returned, false)]
    public void IsOpen_PerStatus(WithdrawalStatus status, bool open)
    {
        Withdrawal.IsOpenStatus(status).ShouldBe(open);
        InStatus(status).IsOpen.ShouldBe(open);
    }

    private static readonly WithdrawalStatus[] All = Enum.GetValues<WithdrawalStatus>();

    private static IEnumerable<TestCaseData> RefusedTransitions()
    {
        var allowed = new Dictionary<string, WithdrawalStatus[]>
        {
            ["approved"] = [WithdrawalStatus.Requested],
            ["rejected"] = [WithdrawalStatus.Requested],
            ["cancelled"] = [WithdrawalStatus.Requested, WithdrawalStatus.Approved],
            ["sent"] = [WithdrawalStatus.Approved],
            ["failed"] = [WithdrawalStatus.Approved],
            ["paid"] = [WithdrawalStatus.Approved, WithdrawalStatus.Sent],
            ["returned"] = [WithdrawalStatus.Paid]
        };
        foreach (var (action, from) in allowed)
        {
            foreach (var status in All.Except(from))
            {
                yield return new TestCaseData(status, action).SetName($"{action}_From{status}_Throws");
            }
        }
    }

    [TestCaseSource(nameof(RefusedTransitions))]
    public void AnActionFromAnotherStatus_Throws_WithTheR27Message(WithdrawalStatus status, string action)
    {
        var withdrawal = InStatus(status);
        Action act = action switch
        {
            "approved" => () => withdrawal.Approve(Approver, null, At),
            "rejected" => () => withdrawal.Reject(Approver, "No", At),
            "cancelled" => () => withdrawal.Cancel(Approver, null, At),
            "sent" => withdrawal.MarkSent,
            "failed" => () => withdrawal.MarkFailed(PaymentRejectReason.Refused),
            "paid" => () => withdrawal.MarkPaid(1),
            _ => () => withdrawal.MarkReturned(1, null)
        };

        Should.Throw<WithdrawalStatusException>(act).Message.ShouldBe($"This withdrawal is {status} and cannot be {action}.");
        withdrawal.Status.ShouldBe(status);
    }

    private static Withdrawal Requested() => Withdrawal.Request(1, 3, 5, 300m, "USD", "K7M3Q9PXB", null, Requester, At);

    private static Withdrawal InStatus(WithdrawalStatus status)
    {
        var withdrawal = Requested();
        switch (status)
        {
            case WithdrawalStatus.Requested:
                break;
            case WithdrawalStatus.Rejected:
                withdrawal.Reject(Approver, "No", At);
                break;
            case WithdrawalStatus.Cancelled:
                withdrawal.Cancel(Requester, null, At);
                break;
            default:
                withdrawal.Approve(Approver, null, At);
                if (status == WithdrawalStatus.Sent)
                {
                    withdrawal.MarkSent();
                }
                else if (status == WithdrawalStatus.Failed)
                {
                    withdrawal.MarkFailed(PaymentRejectReason.Refused);
                }
                else if (status is WithdrawalStatus.Paid or WithdrawalStatus.Returned)
                {
                    withdrawal.MarkPaid(99);
                    if (status == WithdrawalStatus.Returned)
                    {
                        withdrawal.MarkReturned(100, "Account closed");
                    }
                }

                break;
        }

        return withdrawal;
    }
}
