using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Deposit matching R32, R33: <c>Transaction.PostBankDeposit</c>, the bank receipt detail and the flow's Reversal.</summary>
public class BankDepositTests
{
    private const int TenantId = 3;
    private const int AccountId = 11;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 20, 2, 1, 8, TimeSpan.Zero);
    private static readonly Guid LineId = Guid.Parse("3f2c0000-0000-0000-0000-0000000000e1");
    private static readonly BankReceipt Receipt = new(new DateOnly(2026, 11, 20), "K7M3Q9PXB", "J SMITH", "****4200");

    [Test]
    public void PostBankDeposit_IsOneTransferInLeg_WithTheBankFeedSource_TheMemoAndTheDetail()
    {
        var deposit = Deposit(500.00m);

        deposit.Type.ShouldBe(TransactionType.TransferIn);
        deposit.TenantId.ShouldBe(TenantId);
        deposit.BookedAt.ShouldBe(BookedAt);
        deposit.Memo.ShouldBe("Bank deposit");
        deposit.Reference.ShouldBeNull();
        deposit.Source.ShouldBe(TransactionSource.BankFeed(LineId));
        deposit.Source.Kind.ShouldBe(TransactionSourceKind.BankFeed);
        deposit.Source.PublicId.ShouldBe(LineId);
        var leg = deposit.CashLegs.ShouldHaveSingleItem();
        leg.AccountId.ShouldBe(AccountId);
        leg.Amount.ShouldBe(500.00m);
        leg.Currency.ShouldBe("USD");
        leg.Role.ShouldBe(CashLegRole.Principal);
        var detail = deposit.BankReceiptDetail.ShouldNotBeNull();
        detail.TenantId.ShouldBe(TenantId);
        detail.ValueDate.ShouldBe(new DateOnly(2026, 11, 20));
        detail.Reference.ShouldBe("K7M3Q9PXB");
        detail.PayerName.ShouldBe("J SMITH");
        detail.PayerAccount.ShouldBe("****4200");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PostBankDeposit_RefusesAnAmountNotAboveZero(decimal amount) =>
        Should.Throw<DomainException>(() => Deposit(amount));

    [Test]
    public void PostBankDeposit_RefusesAnotherSource()
    {
        Should.Throw<DomainException>(() => Transaction.PostBankDeposit(
            TenantId, AccountId, "USD", 1m, BookedAt, TransactionSource.Manual, Receipt));
        Should.Throw<DomainException>(() => Transaction.PostBankDeposit(
            TenantId, AccountId, "USD", 1m, BookedAt, TransactionSource.Rebalance(Guid.NewGuid()), Receipt));
    }

    [Test]
    public void BankFeedSource_NeedsTheLineId() =>
        Should.Throw<DomainException>(() => TransactionSource.BankFeed(Guid.Empty));

    [Test]
    public void Reverse_ByTheFlow_CopiesTheDetail_AndTakesTheBankFeedSource()
    {
        var deposit = Deposit(500.00m);

        var reversal = deposit.Reverse(
            TransactionSource.BankFeed(LineId), BookedAt.AddHours(1), new Dictionary<int, decimal> { [AccountId] = 500.00m });

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.Source.ShouldBe(TransactionSource.BankFeed(LineId));
        reversal.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-500.00m);
        var detail = reversal.BankReceiptDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(deposit.BankReceiptDetail);
        (detail.ValueDate, detail.Reference, detail.PayerName, detail.PayerAccount)
            .ShouldBe((new DateOnly(2026, 11, 20), "K7M3Q9PXB", "J SMITH", "****4200"));
    }

    [Test]
    public void Reverse_WhenTheCashIsGone_Throws() =>
        Should.Throw<DomainException>(() => Deposit(500.00m).Reverse(
            TransactionSource.BankFeed(LineId), BookedAt, new Dictionary<int, decimal> { [AccountId] = 499.99m }));

    [Test]
    public void OtherTransactions_HaveNoBankReceipt()
    {
        var transfer = Transaction.Post(
            TenantId, AccountId, TransactionType.TransferIn, 5m, "USD", BookedAt, null, null, TransactionSource.Manual, 0m, 0);

        transfer.BankReceiptDetail.ShouldBeNull();
        transfer.Reverse(TransactionSource.Manual, BookedAt, new Dictionary<int, decimal> { [AccountId] = 5m })
            .BankReceiptDetail.ShouldBeNull();
    }

    private static Transaction Deposit(decimal amount) => Transaction.PostBankDeposit(
        TenantId, AccountId, "USD", amount, BookedAt, TransactionSource.BankFeed(LineId), Receipt);
}
