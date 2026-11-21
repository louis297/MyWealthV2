using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Withdrawals R40–R44: <c>Transaction.PostPayout</c>, the payment detail, the flow's Reversal, the source
/// kind <c>Payment</c> and the statement line marks of a payout and a returned payout.</summary>
public class PayoutTests
{
    private const int TenantId = 3;
    private const int AccountId = 11;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 20, 4, 7, 2, TimeSpan.Zero);
    private static readonly Guid WithdrawalId = Guid.Parse("6b1f0000-0000-0000-0000-0000000000a1");
    private static readonly BankPayment Payment = new(new DateOnly(2026, 11, 20), "K7M3Q9PXB", "J Smith", "****8900");

    [Test]
    public void PaymentSource_NeedsTheWithdrawalsId_AndKeepsIt()
    {
        Should.Throw<DomainException>(() => TransactionSource.Payment(Guid.Empty));
        var source = TransactionSource.Payment(WithdrawalId);
        source.Kind.ShouldBe(TransactionSourceKind.Payment);
        source.PublicId.ShouldBe(WithdrawalId);
    }

    [Test]
    public void PostPayout_IsOneNegativeTransferOutLeg_WithThePaymentSource_TheMemoAndTheDetail()
    {
        var payout = Payout(300.00m);

        payout.Type.ShouldBe(TransactionType.TransferOut);
        payout.TenantId.ShouldBe(TenantId);
        payout.BookedAt.ShouldBe(BookedAt);
        payout.Memo.ShouldBe("Withdrawal");
        payout.Reference.ShouldBeNull();
        payout.Source.ShouldBe(TransactionSource.Payment(WithdrawalId));
        var leg = payout.CashLegs.ShouldHaveSingleItem();
        leg.AccountId.ShouldBe(AccountId);
        leg.Amount.ShouldBe(-300.00m);
        leg.Currency.ShouldBe("USD");
        leg.Role.ShouldBe(CashLegRole.Principal);
        var detail = payout.PaymentDetail.ShouldNotBeNull();
        detail.TenantId.ShouldBe(TenantId);
        (detail.ValueDate, detail.Reference, detail.PayeeName, detail.PayeeAccount)
            .ShouldBe((new DateOnly(2026, 11, 20), "K7M3Q9PXB", "J Smith", "****8900"));
        payout.BankReceiptDetail.ShouldBeNull();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PostPayout_RefusesAnAmountNotAboveZero(decimal amount) =>
        Should.Throw<DomainException>(() => Payout(amount));

    [Test]
    public void PostPayout_RefusesAnotherSource()
    {
        Should.Throw<DomainException>(() => Transaction.PostPayout(
            TenantId, AccountId, "USD", 1m, BookedAt, TransactionSource.Manual, Payment));
        Should.Throw<DomainException>(() => Transaction.PostPayout(
            TenantId, AccountId, "USD", 1m, BookedAt, TransactionSource.BankFeed(Guid.NewGuid()), Payment));
    }

    [Test]
    public void PostPayout_RefusesADetailLongerThanItsColumns() =>
        Should.Throw<DomainException>(() => Transaction.PostPayout(
            TenantId, AccountId, "USD", 1m, BookedAt, TransactionSource.Payment(WithdrawalId),
            Payment with { Reference = new string('r', 19) }));

    [Test]
    public void Reverse_ByTheFlow_CopiesTheDetail_AndTakesThePaymentSource()
    {
        var payout = Payout(300.00m);

        var reversal = payout.Reverse(
            TransactionSource.Payment(WithdrawalId), BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 0m });

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.Source.ShouldBe(TransactionSource.Payment(WithdrawalId));
        reversal.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(300.00m);
        var detail = reversal.PaymentDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(payout.PaymentDetail);
        (detail.ValueDate, detail.Reference, detail.PayeeName, detail.PayeeAccount)
            .ShouldBe((new DateOnly(2026, 11, 20), "K7M3Q9PXB", "J Smith", "****8900"));
    }

    [Test]
    public void APayment_IsNotHandReversible_OnACustodialAccount() =>
        CustodyRules.AllowsHandReverse(CustodyMode.Custodial, TransactionSourceKind.Payment).ShouldBeFalse();

    [Test]
    public void MarkPayout_MatchesAReceivedDebitLine_ToTheAccountThePayoutAndTheWithdrawal()
    {
        var line = Line(BankLineDirection.Debit);

        line.MarkPayout(AccountId, 50, 7, BookedAt);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Payment);
        line.MatchedAccountId.ShouldBe(AccountId);
        line.MatchedTransactionId.ShouldBe(50);
        line.MatchedAt.ShouldBe(BookedAt);
        line.WithdrawalId.ShouldBe(7);
    }

    [Test]
    public void MarkPayout_RefusesACreditLine_AndALineNotReceived()
    {
        Should.Throw<DomainException>(() => Line(BankLineDirection.Credit).MarkPayout(AccountId, 50, 7, BookedAt));
        var line = Line(BankLineDirection.Debit);
        line.MarkPaymentMismatch();
        Should.Throw<DomainException>(() => line.MarkPayout(AccountId, 50, 7, BookedAt));
    }

    [Test]
    public void MarkPaymentReturn_MatchesAReceivedCreditLine_AndLinksTheReturnedLine()
    {
        var line = Line(BankLineDirection.Credit);

        line.MarkPaymentReturn(AccountId, 51, 7, 40, BookedAt);

        line.Status.ShouldBe(BankStatementLineStatus.Matched);
        line.MatchMethod.ShouldBe(StatementLineMatchMethod.PaymentReturn);
        line.MatchedTransactionId.ShouldBe(51);
        line.WithdrawalId.ShouldBe(7);
        line.RelatedLineId.ShouldBe(40);
        Should.Throw<DomainException>(() => Line(BankLineDirection.Debit).MarkPaymentReturn(AccountId, 51, 7, 40, BookedAt));
    }

    [TestCase(BankLineDirection.Debit)]
    [TestCase(BankLineDirection.Credit)]
    public void MarkPaymentMismatch_LeavesAReceivedLineUnmatched(BankLineDirection direction)
    {
        var line = Line(direction);

        line.MarkPaymentMismatch();

        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.PaymentMismatch);
        Should.Throw<DomainException>(line.MarkPaymentMismatch);
    }

    [Test]
    public void AReturnedPayoutLine_CannotBeUnmatchedByHand()
    {
        var line = Line(BankLineDirection.Credit);
        line.MarkPaymentReturn(AccountId, 51, 7, 40, BookedAt);

        Should.Throw<StatementLineStatusException>(line.EnsureCanUnmatch);
    }

    private static Transaction Payout(decimal amount) =>
        Transaction.PostPayout(TenantId, AccountId, "USD", amount, BookedAt, TransactionSource.Payment(WithdrawalId), Payment);

    private static BankStatementLine Line(BankLineDirection direction) =>
        BankStatementLine.Receive(
            TenantId, 9, "simulator", "w-L" + Guid.NewGuid().ToString("N")[..6], new DateOnly(2026, 11, 20), direction, 300m, "USD",
            "J Smith", "****8900", "K7M3Q9PXB", BookedAt, endToEndId: WithdrawalId.ToString());
}
