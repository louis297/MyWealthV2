using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Account closure R8–R10, R17, R19, R20 (docs/features/account-closure.md §5): the account closure aggregate.</summary>
public class AccountClosureTests
{
    private const int Requester = 11;
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = At.AddMinutes(12);

    [Test]
    public void StartWithPayout_IsInProgress_AndKeepsTheRecord()
    {
        var publicId = Guid.NewGuid();

        var closure = AccountClosure.StartWithPayout(
            7, 3, " usd ", Requester, At, "  Moving to another provider ", 300.004m, 300.00m, 5, 9, publicId);

        closure.TenantId.ShouldBe(7);
        closure.AccountId.ShouldBe(3);
        closure.Currency.ShouldBe("USD");
        closure.RequestedByUserId.ShouldBe(Requester);
        closure.RequestedAt.ShouldBe(At);
        closure.Memo.ShouldBe("Moving to another provider");
        closure.CashBalanceAtRequest.ShouldBe(300.004m);
        closure.PayoutAmount.ShouldBe(300.00m);
        closure.NominatedBankAccountId.ShouldBe(5);
        closure.WithdrawalId.ShouldBe(9);
        closure.PublicId.ShouldBe(publicId);
        closure.Status.ShouldBe(AccountClosureStatus.InProgress);
        closure.IsInProgress.ShouldBeTrue();
        closure.CancelReason.ShouldBeNull();
        closure.WriteOffAmount.ShouldBeNull();
        closure.WriteOffTransactionId.ShouldBeNull();
        closure.CompletedAt.ShouldBeNull();
        closure.EndedAt.ShouldBeNull();
        closure.DomainEvents.ShouldBeEmpty();
    }

    [TestCase("0")]
    [TestCase("-1")]
    public void StartWithPayout_RefusesAPayoutNotAboveZero(string payout) =>
        Should.Throw<DomainException>(() => AccountClosure.StartWithPayout(
            7, 3, "USD", Requester, At, "Memo", 10m, decimal.Parse(payout), 5, 9));

    [Test]
    public void StartWithPayout_RefusesAPayoutAboveTheCashBalance() =>
        Should.Throw<DomainException>(() => AccountClosure.StartWithPayout(
            7, 3, "USD", Requester, At, "Memo", 10m, 10.01m, 5, 9));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void AClosure_NeedsAMemo(string? memo) =>
        Should.Throw<DomainException>(() => AccountClosure.StartWithPayout(
            7, 3, "USD", Requester, At, memo!, 10m, 10m, 5, 9));

    [Test]
    public void AClosure_RefusesAMemoAbove200Characters()
    {
        Should.NotThrow(() => AccountClosure.CompleteAtOnce(7, 3, "USD", Requester, At, new string('m', 200), 0m, null, null));
        Should.Throw<DomainException>(() => AccountClosure.CompleteAtOnce(
            7, 3, "USD", Requester, At, new string('m', 201), 0m, null, null));
    }

    [Test]
    public void CompleteAtOnce_WithAWriteOff_IsCompleted_WithNoPayoutOrWithdrawal()
    {
        var closure = AccountClosure.CompleteAtOnce(7, 3, "USD", Requester, At, "Flat", 0.004m, 0.004m, 41);

        closure.Status.ShouldBe(AccountClosureStatus.Completed);
        closure.IsInProgress.ShouldBeFalse();
        closure.CashBalanceAtRequest.ShouldBe(0.004m);
        closure.PayoutAmount.ShouldBe(0m);
        closure.NominatedBankAccountId.ShouldBeNull();
        closure.WithdrawalId.ShouldBeNull();
        closure.WriteOffAmount.ShouldBe(0.004m);
        closure.WriteOffTransactionId.ShouldBe(41);
        closure.CompletedAt.ShouldBe(At);
        closure.EndedAt.ShouldBeNull();
    }

    [Test]
    public void CompleteAtOnce_WithNothing_IsCompleted_WithNoWriteOff()
    {
        var closure = AccountClosure.CompleteAtOnce(7, 3, "USD", Requester, At, "Flat", 0m, null, null);

        closure.Status.ShouldBe(AccountClosureStatus.Completed);
        closure.WriteOffAmount.ShouldBeNull();
        closure.WriteOffTransactionId.ShouldBeNull();
        closure.CompletedAt.ShouldBe(At);
    }

    [TestCase("0.004", null)]
    [TestCase(null, 41)]
    public void TheWriteOffAmountAndItsTransaction_AreSetTogether(string? amount, int? transactionId)
    {
        var writeOff = amount is null ? (decimal?)null : decimal.Parse(amount);

        Should.Throw<DomainException>(() => AccountClosure.CompleteAtOnce(
            7, 3, "USD", Requester, At, "Flat", 0.004m, writeOff, transactionId));
        Should.Throw<DomainException>(() => Started().Complete(writeOff, transactionId, Later));
    }

    [Test]
    public void AWriteOff_IsAboveZero() =>
        Should.Throw<DomainException>(() => Started().Complete(0m, 41, Later));

    [Test]
    public void CompleteAtOnce_RefusesACashBalanceWithAPayout() =>
        Should.Throw<DomainException>(() => AccountClosure.CompleteAtOnce(
            7, 3, "USD", Requester, At, "Flat", 0.01m, null, null));

    [Test]
    public void Complete_FromInProgress_IsCompleted_WithTheWriteOff()
    {
        var closure = Started();

        closure.Complete(0.004m, 41, Later);

        closure.Status.ShouldBe(AccountClosureStatus.Completed);
        closure.WriteOffAmount.ShouldBe(0.004m);
        closure.WriteOffTransactionId.ShouldBe(41);
        closure.CompletedAt.ShouldBe(Later);
        closure.EndedAt.ShouldBeNull();
        closure.CancelReason.ShouldBeNull();
    }

    [Test]
    public void Complete_WithNoRemainder_HasNoWriteOff()
    {
        var closure = Started();

        closure.Complete(null, null, Later);

        closure.Status.ShouldBe(AccountClosureStatus.Completed);
        closure.WriteOffAmount.ShouldBeNull();
    }

    [TestCase(AccountClosureCancelReason.WithdrawalRejected)]
    [TestCase(AccountClosureCancelReason.WithdrawalCancelled)]
    [TestCase(AccountClosureCancelReason.PaymentFailed)]
    [TestCase(AccountClosureCancelReason.NotClosable)]
    public void Cancel_FromInProgress_IsCancelled_WithTheReason(AccountClosureCancelReason reason)
    {
        var closure = Started();

        closure.Cancel(reason, Later);

        closure.Status.ShouldBe(AccountClosureStatus.Cancelled);
        closure.IsInProgress.ShouldBeFalse();
        closure.CancelReason.ShouldBe(reason);
        closure.EndedAt.ShouldBe(Later);
        closure.CompletedAt.ShouldBeNull();
    }

    [Test]
    public void Cancel_NeedsAKnownReason() =>
        Should.Throw<DomainException>(() => Started().Cancel((AccountClosureCancelReason)99, Later));

    [Test]
    public void ACompletedClosure_CannotCompleteOrCancel()
    {
        var closure = Started();
        closure.Complete(null, null, Later);

        Should.Throw<DomainException>(() => closure.Complete(null, null, Later));
        Should.Throw<DomainException>(() => closure.Cancel(AccountClosureCancelReason.WithdrawalCancelled, Later));
    }

    [Test]
    public void ACancelledClosure_CannotCompleteOrCancel()
    {
        var closure = Started();
        closure.Cancel(AccountClosureCancelReason.WithdrawalRejected, Later);

        Should.Throw<DomainException>(() => closure.Complete(null, null, Later));
        Should.Throw<DomainException>(() => closure.Cancel(AccountClosureCancelReason.PaymentFailed, Later));
    }

    [Test]
    public void AClosureCompletedAtOnce_CannotCompleteOrCancel()
    {
        var closure = AccountClosure.CompleteAtOnce(7, 3, "USD", Requester, At, "Flat", 0m, null, null);

        Should.Throw<DomainException>(() => closure.Complete(null, null, Later));
        Should.Throw<DomainException>(() => closure.Cancel(AccountClosureCancelReason.NotClosable, Later));
    }

    private static AccountClosure Started() =>
        AccountClosure.StartWithPayout(7, 3, "USD", Requester, At, "Moving", 300.004m, 300.00m, 5, 9);
}
