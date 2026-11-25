using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.AdjustmentRequests;

/// <summary>Adjustment review R2, R6–R11, R13 (docs/features/adjustment-review.md §5): the adjustment request
/// aggregate and its Transaction source.</summary>
public class AdjustmentRequestTests
{
    private const int Requester = 11;
    private const int Approver = 22;
    private const int TransactionId = 31;
    private static readonly DateTimeOffset At = new(2026, 11, 25, 2, 0, 0, TimeSpan.Zero);

    [Test]
    public void Request_CashOnly_IsRequested_Open_AndKeepsTrimmedFields()
    {
        var publicId = Guid.NewGuid();

        var request = AdjustmentRequest.Request(7, 3, AdjustmentReason.EntryError, "  Wrong deposit amount ", " REF-1 ",
            -0.60m, null, Requester, At, publicId);

        (request.TenantId, request.AccountId).ShouldBe((7, 3));
        request.Reason.ShouldBe(AdjustmentReason.EntryError);
        request.Memo.ShouldBe("Wrong deposit amount");
        request.Reference.ShouldBe("REF-1");
        request.CashAmount.ShouldBe(-0.60m);
        (request.InstrumentId, request.QuantityChange, request.CostChange).ShouldBe(((int?)null, (decimal?)null, (decimal?)null));
        request.Status.ShouldBe(AdjustmentRequestStatus.Requested);
        request.IsOpen.ShouldBeTrue();
        (request.RequestedByUserId, request.RequestedAt).ShouldBe((Requester, At));
        request.PublicId.ShouldBe(publicId);
        request.DecisionAction.ShouldBeNull();
        request.DecisionByUserId.ShouldBeNull();
        request.DecisionAt.ShouldBeNull();
        request.DecisionNote.ShouldBeNull();
        request.TransactionId.ShouldBeNull();
        request.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Request_HoldingOnly_KeepsTheInstrumentAndBothChanges()
    {
        var request = AdjustmentRequest.Request(7, 3, AdjustmentReason.CorporateAction, "Split by hand", null,
            null, (9, 10m, 0m), Requester, At);

        request.CashAmount.ShouldBeNull();
        (request.InstrumentId, request.QuantityChange, request.CostChange).ShouldBe(((int?)9, (decimal?)10m, (decimal?)0m));
        request.Reference.ShouldBeNull();
        request.PublicId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void Request_CashAndHolding_KeepsBoth()
    {
        var request = AdjustmentRequest.Request(7, 3, AdjustmentReason.EntryError, "Fill price", null,
            -0.60m, (9, 0m, 0.60m), Requester, At);

        request.CashAmount.ShouldBe(-0.60m);
        (request.InstrumentId, request.QuantityChange, request.CostChange).ShouldBe(((int?)9, (decimal?)0m, (decimal?)0.60m));
    }

    [Test]
    public void Request_WithNeitherPart_Throws() =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, AdjustmentReason.Other, "Nothing", null,
            null, null, Requester, At));

    [Test]
    public void Request_WithAZeroCashAmount_Throws() =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, AdjustmentReason.Other, "Zero", null,
            0m, null, Requester, At));

    [Test]
    public void Request_WithAHoldingOfTwoZeroChanges_Throws() =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, AdjustmentReason.Other, "Zero", null,
            null, (9, 0m, 0m), Requester, At));

    [TestCase("")]
    [TestCase("   ")]
    public void Request_WithABlankMemo_Throws(string memo) =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, AdjustmentReason.Other, memo, null,
            5m, null, Requester, At));

    [Test]
    public void Request_WithAMemoAbove200Characters_Throws() =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, AdjustmentReason.Other, new string('m', 201), null,
            5m, null, Requester, At));

    [Test]
    public void Request_WithAnUndefinedReason_Throws() =>
        Should.Throw<DomainException>(() => AdjustmentRequest.Request(7, 3, (AdjustmentReason)9, "Memo", null,
            5m, null, Requester, At));

    [Test]
    public void Approve_ByAnotherPerson_IsApproved_WithTheDecisionAndTheTransaction()
    {
        var request = Requested();

        request.Approve(Approver, At.AddMinutes(10), "  Checked against the contract note ", TransactionId);

        request.Status.ShouldBe(AdjustmentRequestStatus.Approved);
        request.IsOpen.ShouldBeFalse();
        request.DecisionAction.ShouldBe(AdjustmentRequestStatus.Approved);
        request.DecisionByUserId.ShouldBe(Approver);
        request.DecisionAt.ShouldBe(At.AddMinutes(10));
        request.DecisionNote.ShouldBe("Checked against the contract note");
        request.TransactionId.ShouldBe(TransactionId);
    }

    [Test]
    public void Approve_WithNoNote_KeepsNoNote()
    {
        var request = Requested();

        request.Approve(Approver, At, "  ", TransactionId);

        request.DecisionNote.ShouldBeNull();
    }

    [Test]
    public void Approve_ByTheRequester_Throws()
    {
        var request = Requested();

        Should.Throw<AdjustmentRequestStatusException>(() => request.Approve(Requester, At, null, TransactionId))
            .Message.ShouldBe(AdjustmentRequest.FourEyesMessage);
        request.Status.ShouldBe(AdjustmentRequestStatus.Requested);
        request.TransactionId.ShouldBeNull();
    }

    [Test]
    public void Reject_ByAnotherPerson_WithANote_IsRejected_WithNoTransaction()
    {
        var request = Requested();

        request.Reject(Approver, At.AddMinutes(5), " Wrong account ");

        request.Status.ShouldBe(AdjustmentRequestStatus.Rejected);
        request.DecisionAction.ShouldBe(AdjustmentRequestStatus.Rejected);
        (request.DecisionByUserId, request.DecisionAt, request.DecisionNote).ShouldBe(((int?)Approver, (DateTimeOffset?)At.AddMinutes(5), "Wrong account"));
        request.TransactionId.ShouldBeNull();
    }

    [Test]
    public void Reject_ByTheRequester_Throws() =>
        Should.Throw<AdjustmentRequestStatusException>(() => Requested().Reject(Requester, At, "Mine"))
            .Message.ShouldBe(AdjustmentRequest.FourEyesMessage);

    [TestCase(null)]
    [TestCase("  ")]
    public void Reject_WithNoNote_Throws(string? note) =>
        Should.Throw<DomainException>(() => Requested().Reject(Approver, At, note!));

    [Test]
    public void Cancel_ByTheRequester_IsCancelled()
    {
        var request = Requested();

        request.Cancel(Requester, At.AddMinutes(1), null);

        request.Status.ShouldBe(AdjustmentRequestStatus.Cancelled);
        request.DecisionAction.ShouldBe(AdjustmentRequestStatus.Cancelled);
        (request.DecisionByUserId, request.DecisionAt).ShouldBe(((int?)Requester, (DateTimeOffset?)At.AddMinutes(1)));
        request.DecisionNote.ShouldBeNull();
        request.TransactionId.ShouldBeNull();
    }

    [Test]
    public void Cancel_ByAnotherPerson_KeepsTheNote()
    {
        var request = Requested();

        request.Cancel(Approver, At, "No longer needed");

        request.Status.ShouldBe(AdjustmentRequestStatus.Cancelled);
        request.DecisionNote.ShouldBe("No longer needed");
    }

    [Test]
    public void ANoteAbove200Characters_Throws() =>
        Should.Throw<DomainException>(() => Requested().Approve(Approver, At, new string('n', 201), TransactionId));

    private static IEnumerable<TestCaseData> EndStatuses()
    {
        yield return new TestCaseData(new Action<AdjustmentRequest>(r => r.Approve(Approver, At, null, TransactionId)), "Approved")
            .SetArgDisplayNames("Approved");
        yield return new TestCaseData(new Action<AdjustmentRequest>(r => r.Reject(Approver, At, "No")), "Rejected")
            .SetArgDisplayNames("Rejected");
        yield return new TestCaseData(new Action<AdjustmentRequest>(r => r.Cancel(Requester, At, null)), "Cancelled")
            .SetArgDisplayNames("Cancelled");
    }

    [TestCaseSource(nameof(EndStatuses))]
    public void EveryAction_FromAnEndStatus_Throws(Action<AdjustmentRequest> end, string status)
    {
        var request = Requested();
        end(request);

        Should.Throw<AdjustmentRequestStatusException>(() => request.Approve(Approver, At, null, TransactionId))
            .Message.ShouldBe($"This adjustment request is {status} and cannot be approved.");
        Should.Throw<AdjustmentRequestStatusException>(() => request.Reject(Approver, At, "No"))
            .Message.ShouldBe($"This adjustment request is {status} and cannot be rejected.");
        Should.Throw<AdjustmentRequestStatusException>(() => request.Cancel(Requester, At, null))
            .Message.ShouldBe($"This adjustment request is {status} and cannot be cancelled.");
        request.Status.ToString().ShouldBe(status);
    }

    [Test]
    public void Source_AdjustmentRequest_KeepsTheRequestsPublicId()
    {
        var id = Guid.NewGuid();

        var source = TransactionSource.AdjustmentRequest(id);

        source.Kind.ShouldBe(TransactionSourceKind.AdjustmentRequest);
        source.PublicId.ShouldBe(id);
        ((int)TransactionSourceKind.AdjustmentRequest).ShouldBe(11);
    }

    [Test]
    public void Source_AdjustmentRequest_NeedsAnId() =>
        Should.Throw<DomainException>(() => TransactionSource.AdjustmentRequest(Guid.Empty));

    private static AdjustmentRequest Requested() =>
        AdjustmentRequest.Request(7, 3, AdjustmentReason.EntryError, "Wrong deposit amount", null, -5m, null, Requester, At);
}
