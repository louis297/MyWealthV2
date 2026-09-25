using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class TransactionTests
{
    [Test]
    public void Post_TransferIn_StoresSignedAmountAndRaisesTransactionPosted()
    {
        var bookedAt = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(12));

        var transaction = Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.TransferIn,
            amount: 100.00m,
            accountCurrency: "NZD",
            bookedAt: bookedAt,
            memo: "Salary",
            reference: null,
            currentCashSum: 0m,
            existingTransactionCount: 0);

        transaction.TenantId.ShouldBe(1);
        transaction.AccountId.ShouldBe(2);
        transaction.Type.ShouldBe(TransactionType.TransferIn);
        transaction.BookedAt.ShouldBe(bookedAt);
        transaction.Memo.ShouldBe("Salary");
        transaction.Reference.ShouldBeNull();
        transaction.OriginalTransactionId.ShouldBeNull();
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.CashLeg.ShouldNotBeNull();
        transaction.CashLeg!.Amount.ShouldBe(100.00m);
        transaction.CashLeg.Currency.ShouldBe("NZD");
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }

    [TestCase(TransactionType.TransferIn, 0)]
    [TestCase(TransactionType.TransferIn, -1)]
    [TestCase(TransactionType.Opening, 0)]
    [TestCase(TransactionType.Opening, -5)]
    [TestCase(TransactionType.TransferOut, 0)]
    [TestCase(TransactionType.TransferOut, 20)]
    [TestCase(TransactionType.CloseOut, 0)]
    [TestCase(TransactionType.CloseOut, 20)]
    [TestCase(TransactionType.Interest, 0)]
    public void Post_RejectsAmountWithTheWrongSign(TransactionType type, decimal amount)
    {
        Should.Throw<DomainException>(() => Post(type, amount, currentCashSum: 100m, existingTransactionCount: 0));
    }

    [TestCase(TransactionType.Dividend, 10)]
    [TestCase(TransactionType.Buy, -10)]
    [TestCase(TransactionType.Sell, 10)]
    [TestCase(TransactionType.Reversal, -10)]
    public void Post_RejectsTypesOutsideTheCashIncrement(TransactionType type, decimal amount)
    {
        Should.Throw<DomainException>(() => Post(type, amount, currentCashSum: 100m, existingTransactionCount: 0));
    }

    [Test]
    public void Post_TransferOut_StoresNegativeAmount()
    {
        var transaction = Post(TransactionType.TransferOut, -40m, currentCashSum: 40m, existingTransactionCount: 0);

        transaction.CashLeg!.Amount.ShouldBe(-40m);
        transaction.Type.ShouldBe(TransactionType.TransferOut);
    }

    [Test]
    public void Post_Opening_RejectsWhenTheAccountAlreadyHasTransactions()
    {
        Should.Throw<DomainException>(() => Post(TransactionType.Opening, 100m, currentCashSum: 0m, existingTransactionCount: 1));
    }

    [Test]
    public void Post_CloseOut_RequiresTheAmountToZeroTheCashSum()
    {
        Should.Throw<DomainException>(() => Post(TransactionType.CloseOut, -40m, currentCashSum: 100m, existingTransactionCount: 1));

        var transaction = Post(TransactionType.CloseOut, -100m, currentCashSum: 100m, existingTransactionCount: 1);

        transaction.CashLeg!.Amount.ShouldBe(-100m);
        transaction.Type.ShouldBe(TransactionType.CloseOut);
    }

    [Test]
    public void Post_RejectsWhenCashSumWouldBecomeNegative()
    {
        Should.Throw<DomainException>(() => Post(TransactionType.TransferOut, -20m, currentCashSum: 10m, existingTransactionCount: 1));
        Should.Throw<DomainException>(() => Post(TransactionType.Interest, -15m, currentCashSum: 10m, existingTransactionCount: 1));
    }

    [TestCase(-2.5)]
    [TestCase(2.5)]
    public void Post_Interest_AllowsEitherSign(decimal amount)
    {
        var transaction = Post(TransactionType.Interest, amount, currentCashSum: 10m, existingTransactionCount: 1);

        transaction.CashLeg!.Amount.ShouldBe(amount);
        transaction.Type.ShouldBe(TransactionType.Interest);
    }

    [Test]
    public void Post_TrimsMemoAndClearsBlankReference()
    {
        var transaction = Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.TransferIn,
            amount: 10m,
            accountCurrency: "nzd",
            bookedAt: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(12)),
            memo: "  Salary  ",
            reference: "   ",
            currentCashSum: 0m,
            existingTransactionCount: 0);

        transaction.Memo.ShouldBe("Salary");
        transaction.Reference.ShouldBeNull();
        transaction.CashLeg!.Currency.ShouldBe("NZD");
    }

    [Test]
    public void Post_RejectsEmptyOrTooLongMemoAndTooLongReference()
    {
        Should.Throw<DomainException>(() => PostWithText(memo: " ", reference: null));
        Should.Throw<DomainException>(() => PostWithText(memo: new string('a', 201), reference: null));
        Should.Throw<DomainException>(() => PostWithText(memo: null, reference: new string('b', 101)));
    }

    [Test]
    public void Reverse_CreatesOppositeTransactionAndLeavesOriginalUntouched()
    {
        var original = Post(TransactionType.TransferIn, 100m, currentCashSum: 0m, existingTransactionCount: 0);
        original.Id = 7;
        original.ClearDomainEvents();
        var bookedAt = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

        var reversal = original.Reverse(bookedAt, currentCashSum: 100m);

        original.Type.ShouldBe(TransactionType.TransferIn);
        original.CashLeg!.Amount.ShouldBe(100m);
        original.DomainEvents.ShouldBeEmpty();
        reversal.ShouldNotBeSameAs(original);
        reversal.PublicId.ShouldNotBe(original.PublicId);
        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.TenantId.ShouldBe(original.TenantId);
        reversal.AccountId.ShouldBe(original.AccountId);
        reversal.BookedAt.ShouldBe(bookedAt);
        reversal.OriginalTransactionId.ShouldBe(7);
        reversal.Memo.ShouldBeNull();
        reversal.Reference.ShouldBeNull();
        reversal.CashLeg!.Amount.ShouldBe(-100m);
        reversal.CashLeg.Currency.ShouldBe("NZD");
        reversal.DomainEvents.OfType<TransactionReversed>().ShouldHaveSingleItem();
        reversal.DomainEvents.OfType<TransactionPosted>().ShouldBeEmpty();
    }

    [Test]
    public void Reverse_RejectsReversalOfAReversal()
    {
        var original = Post(TransactionType.TransferIn, 100m, currentCashSum: 0m, existingTransactionCount: 0);
        original.Id = 7;
        var reversal = original.Reverse(
            new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero),
            currentCashSum: 100m);

        Should.Throw<DomainException>(() => reversal.Reverse(
            new DateTimeOffset(2026, 9, 25, 11, 0, 0, TimeSpan.Zero),
            currentCashSum: 0m));
    }

    [Test]
    public void Reverse_RejectsWhenCashSumWouldBecomeNegative()
    {
        var original = Post(TransactionType.TransferIn, 100m, currentCashSum: 0m, existingTransactionCount: 0);

        Should.Throw<DomainException>(() => original.Reverse(
            new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero),
            currentCashSum: 20m));
    }

    private static Transaction PostWithText(string? memo, string? reference) =>
        Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.TransferIn,
            amount: 10m,
            accountCurrency: "NZD",
            bookedAt: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(12)),
            memo: memo,
            reference: reference,
            currentCashSum: 0m,
            existingTransactionCount: 0);

    private static Transaction Post(
        TransactionType type,
        decimal amount,
        decimal currentCashSum,
        int existingTransactionCount) =>
        Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: type,
            amount: amount,
            accountCurrency: "NZD",
            bookedAt: new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(12)),
            memo: null,
            reference: null,
            currentCashSum: currentCashSum,
            existingTransactionCount: existingTransactionCount);
}
