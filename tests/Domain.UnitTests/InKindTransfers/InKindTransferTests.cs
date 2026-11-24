using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.InKindTransfers;

/// <summary>In-kind transfers R21, R25, R27, R30–R36 (docs/features/in-kind-transfers.md §5): the transfer aggregate.</summary>
public class InKindTransferTests
{
    private const int Requester = 11;
    private const int Approver = 22;
    private const int SafekeepingAccountId = 4;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public void Request_In_IsRequested_Open_AndKeepsTrimmedFields()
    {
        var publicId = Guid.NewGuid();

        var transfer = InKindTransfer.Request(7, 3, 9, TransferDirection.In, 100m, " usd ", 9500m, new DateOnly(2024, 3, 18),
            false, " Harbour Brokers ", " HB-0044172 ", " J Smith ", "  Moving it across ", Requester, At, publicId);

        (transfer.TenantId, transfer.AccountId, transfer.InstrumentId).ShouldBe((7, 3, 9));
        transfer.Direction.ShouldBe(TransferDirection.In);
        transfer.Quantity.ShouldBe(100m);
        transfer.Currency.ShouldBe("USD");
        (transfer.Cost, transfer.AcquiredOn).ShouldBe(((decimal?)9500m, (DateOnly?)new DateOnly(2024, 3, 18)));
        transfer.ChangesBeneficialOwner.ShouldBeFalse();
        (transfer.CounterpartyName, transfer.CounterpartyAccount, transfer.CounterpartyHolderName)
            .ShouldBe(("Harbour Brokers", "HB-0044172", "J Smith"));
        transfer.Memo.ShouldBe("Moving it across");
        transfer.Status.ShouldBe(InKindTransferStatus.Requested);
        (transfer.RequestedByUserId, transfer.RequestedAt).ShouldBe((Requester, At));
        transfer.PublicId.ShouldBe(publicId);
        transfer.IsOpen.ShouldBeTrue();
        transfer.ReservedQuantity.ShouldBe(0m);
        transfer.Attempts.ShouldBe(0);
        transfer.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Request_Out_ReservesItsQuantity()
    {
        var transfer = Out();

        (transfer.Cost, transfer.AcquiredOn).ShouldBe(((decimal?)null, (DateOnly?)null));
        transfer.ReservedQuantity.ShouldBe(40m);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Request_RefusesAQuantityNotAboveZero(decimal quantity) =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.Out, quantity, "USD", null, null,
            false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [Test]
    public void Request_RefusesAQuantityWithMoreThan8Decimals() =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.Out, 1.000000001m, "USD", null,
            null, false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [Test]
    public void Request_In_WithoutACost_Throws() =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.In, 1m, "USD", null, null,
            false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [Test]
    public void Request_In_WithACostBelowZero_Throws() =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.In, 1m, "USD", -0.01m, null,
            false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [Test]
    public void Request_In_WithACostOfZero_IsAllowed() =>
        InKindTransfer.Request(1, 1, 1, TransferDirection.In, 1m, "USD", 0m, null, true, "Harbour", "HB-1", "J Smith", null,
            Requester, At).Cost.ShouldBe(0m);

    [Test]
    public void Request_Out_WithACost_Throws() =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.Out, 1m, "USD", 5m, null,
            false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [Test]
    public void Request_Out_WithAnAcquiredOn_Throws() =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.Out, 1m, "USD", null,
            new DateOnly(2024, 1, 1), false, "Harbour", "HB-1", "J Smith", null, Requester, At));

    [TestCase("", "HB-1", "J Smith")]
    [TestCase("Harbour", " ", "J Smith")]
    [TestCase("Harbour", "HB-1", "")]
    [TestCase("Harbour", "HB/1", "J Smith")]
    public void Request_RefusesABlankOrBadCounterpartyField(string name, string account, string holder) =>
        Should.Throw<DomainException>(() => InKindTransfer.Request(1, 1, 1, TransferDirection.Out, 1m, "USD", null, null,
            false, name, account, holder, null, Requester, At));

    [Test]
    public void Approve_ByAnotherPerson_IsApproved_WithTheSafekeepingAccount()
    {
        var transfer = Out();

        transfer.Approve(Approver, At.AddMinutes(5), " Statement sighted ", SafekeepingAccountId);

        transfer.Status.ShouldBe(InKindTransferStatus.Approved);
        transfer.SafekeepingAccountId.ShouldBe(SafekeepingAccountId);
        (transfer.DecisionAction, transfer.DecisionByUserId, transfer.DecisionAt, transfer.DecisionNote)
            .ShouldBe(((InKindTransferDecision?)InKindTransferDecision.Approve, (int?)Approver, (DateTimeOffset?)At.AddMinutes(5), "Statement sighted"));
        transfer.ReservedQuantity.ShouldBe(40m);
    }

    [Test]
    public void Approve_ByTheRequester_Throws() =>
        Should.Throw<InKindTransferStatusException>(() => Out().Approve(Requester, At, null, SafekeepingAccountId))
            .Message.ShouldBe(InKindTransfer.FourEyesMessage);

    [Test]
    public void Reject_ByTheRequester_Throws() =>
        Should.Throw<InKindTransferStatusException>(() => Out().Reject(Requester, At, "No"))
            .Message.ShouldBe(InKindTransfer.FourEyesMessage);

    [Test]
    public void Approve_FromApproved_Throws()
    {
        var transfer = Approved();

        Should.Throw<InKindTransferStatusException>(() => transfer.Approve(Approver, At, null, SafekeepingAccountId))
            .Message.ShouldBe("This transfer is Approved and cannot be approved.");
    }

    [Test]
    public void Reject_NeedsANote_AndReleasesTheReservation()
    {
        Should.Throw<DomainException>(() => Out().Reject(Approver, At, "  "));

        var transfer = Out();
        transfer.Reject(Approver, At, "Wrong account");

        transfer.Status.ShouldBe(InKindTransferStatus.Rejected);
        transfer.DecisionNote.ShouldBe("Wrong account");
        transfer.IsOpen.ShouldBeFalse();
        transfer.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void Cancel_FromRequested_ByTheRequester_IsCancelled()
    {
        var transfer = Out();

        transfer.Cancel(Requester, At, null);

        transfer.Status.ShouldBe(InKindTransferStatus.Cancelled);
        transfer.DecisionAction.ShouldBe(InKindTransferDecision.Cancel);
        transfer.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void Cancel_FromApprovedWithNoAttempt_IsCancelled()
    {
        var transfer = Approved();

        transfer.Cancel(Requester, At, "Customer changed their mind");

        transfer.Status.ShouldBe(InKindTransferStatus.Cancelled);
    }

    [Test]
    public void Cancel_AfterAnAttempt_Throws()
    {
        var transfer = Approved();
        transfer.RecordAttempt(At);

        Should.Throw<InKindTransferStatusException>(() => transfer.Cancel(Requester, At, null))
            .Message.ShouldBe(InKindTransfer.CancelAfterAttemptMessage);
    }

    [Test]
    public void RecordAttempt_CountsAndTimesEachAttempt()
    {
        var transfer = Approved();

        transfer.RecordAttempt(At);
        transfer.RecordFailure(CustodianFailure.Timeout);
        transfer.RecordAttempt(At.AddMinutes(1));

        (transfer.Attempts, transfer.LastAttemptAt, transfer.LastFailure)
            .ShouldBe((2, (DateTimeOffset?)At.AddMinutes(1), (CustodianFailure?)CustodianFailure.Timeout));
        transfer.Status.ShouldBe(InKindTransferStatus.Approved);
    }

    [Test]
    public void Accept_IsSent_WithTheCustodiansId()
    {
        var transfer = Attempted();

        transfer.Accept("w-T3", At.AddMinutes(2));

        transfer.Status.ShouldBe(InKindTransferStatus.Sent);
        (transfer.CustodianTransferId, transfer.AcceptedAt).ShouldBe(("w-T3", (DateTimeOffset?)At.AddMinutes(2)));
        transfer.IsOpen.ShouldBeTrue();
    }

    [Test]
    public void Accept_OfACompletedTransfer_KeepsItsStatus_AndTakesTheId()
    {
        var transfer = Attempted();
        transfer.Complete(55, At.AddMinutes(3));

        transfer.Accept("w-T3", At.AddMinutes(4));

        transfer.Status.ShouldBe(InKindTransferStatus.Completed);
        transfer.CustodianTransferId.ShouldBe("w-T3");
    }

    [Test]
    public void Fail_IsFailed_WithTheReason_AndReleasesTheReservation()
    {
        var transfer = Attempted();

        transfer.Fail(TransferRejectReason.InsufficientPosition, At);

        transfer.Status.ShouldBe(InKindTransferStatus.Failed);
        transfer.FailureReason.ShouldBe(TransferRejectReason.InsufficientPosition);
        transfer.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void SetAside_FromSent_NeedsANote_AndReleasesTheReservation()
    {
        var transfer = Attempted();
        transfer.Accept("w-T3", At);

        Should.Throw<DomainException>(() => transfer.SetAside(Approver, At, ""));
        transfer.SetAside(Approver, At.AddDays(1), " Never executed ");

        transfer.Status.ShouldBe(InKindTransferStatus.SetAside);
        (transfer.SetAsideByUserId, transfer.SetAsideAt, transfer.SetAsideNote)
            .ShouldBe(((int?)Approver, (DateTimeOffset?)At.AddDays(1), "Never executed"));
        transfer.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void SetAside_FromApprovedWithAnAttempt_IsSetAside()
    {
        var transfer = Attempted();

        transfer.SetAside(Approver, At, "Lost answer");

        transfer.Status.ShouldBe(InKindTransferStatus.SetAside);
    }

    [Test]
    public void SetAside_FromApprovedWithNoAttempt_Throws() =>
        Should.Throw<InKindTransferStatusException>(() => Approved().SetAside(Approver, At, "Note"));

    [Test]
    public void SetAside_FromRequested_Throws() =>
        Should.Throw<InKindTransferStatusException>(() => Out().SetAside(Approver, At, "Note"))
            .Message.ShouldBe("This transfer is Requested and cannot be set aside.");

    [Test]
    public void Complete_FromSent_IsCompleted_WithTheTransaction()
    {
        var transfer = Attempted();
        transfer.Accept("w-T3", At);

        transfer.Complete(55, At.AddMinutes(9));

        transfer.Status.ShouldBe(InKindTransferStatus.Completed);
        (transfer.TransactionId, transfer.CompletedAt).ShouldBe(((int?)55, (DateTimeOffset?)At.AddMinutes(9)));
        transfer.IsOpen.ShouldBeFalse();
        transfer.ReservedQuantity.ShouldBe(0m);
    }

    [Test]
    public void Complete_FromApprovedWithAnAttempt_IsCompleted()
    {
        var transfer = Attempted();

        transfer.Complete(55, At);

        transfer.Status.ShouldBe(InKindTransferStatus.Completed);
    }

    [Test]
    public void Complete_FromApprovedWithNoAttempt_Throws() =>
        Should.Throw<InKindTransferStatusException>(() => Approved().Complete(55, At));

    [Test]
    public void Complete_FromCancelled_Throws()
    {
        var transfer = Out();
        transfer.Cancel(Requester, At, null);

        Should.Throw<InKindTransferStatusException>(() => transfer.Complete(55, At));
    }

    [TestCase(InKindTransferStatus.Requested, true)]
    [TestCase(InKindTransferStatus.Approved, true)]
    [TestCase(InKindTransferStatus.Sent, true)]
    [TestCase(InKindTransferStatus.Completed, false)]
    [TestCase(InKindTransferStatus.Rejected, false)]
    [TestCase(InKindTransferStatus.Cancelled, false)]
    [TestCase(InKindTransferStatus.Failed, false)]
    [TestCase(InKindTransferStatus.SetAside, false)]
    public void IsOpenStatus_IsRequestedApprovedOrSent(InKindTransferStatus status, bool open) =>
        InKindTransfer.IsOpenStatus(status).ShouldBe(open);

    private static InKindTransfer Out() =>
        InKindTransfer.Request(7, 3, 9, TransferDirection.Out, 40m, "USD", null, null, false, "Harbour Brokers", "HB-0044172",
            "J Smith", null, Requester, At);

    private static InKindTransfer Approved()
    {
        var transfer = Out();
        transfer.Approve(Approver, At, null, SafekeepingAccountId);
        return transfer;
    }

    private static InKindTransfer Attempted()
    {
        var transfer = Approved();
        transfer.RecordAttempt(At);
        return transfer;
    }
}
