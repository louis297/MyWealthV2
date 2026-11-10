using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class InternalTransferTests
{
    private const int FromAccountId = 41;
    private const int ToAccountId = 42;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(13));

    [Test]
    public void PostInternalTransfer_BuildsNegativeLegOnFromAndPositiveLegOnTo()
    {
        var transaction = Transfer(500m);

        transaction.Type.ShouldBe(TransactionType.InternalTransfer);
        transaction.TenantId.ShouldBe(1);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("Move to savings");
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.SecurityLegs.ShouldBeEmpty();
        transaction.CashLegs.Count.ShouldBe(2);
        var from = transaction.CashLegs.Single(leg => leg.AccountId == FromAccountId);
        var to = transaction.CashLegs.Single(leg => leg.AccountId == ToAccountId);
        from.Amount.ShouldBe(-500m);
        to.Amount.ShouldBe(500m);
        from.Currency.ShouldBe("NZD");
        to.Currency.ShouldBe("NZD");
        transaction.CashLegs.Sum(leg => leg.Amount).ShouldBe(0m);
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }

    [Test]
    public void PostInternalTransfer_RejectsTheSameAccount()
    {
        Should.Throw<DomainException>(() => Transfer(10m, toAccountId: FromAccountId));
    }

    [Test]
    public void PostInternalTransfer_RejectsDifferentAccountCurrencies()
    {
        Should.Throw<DomainException>(() => Transfer(10m, toCurrency: "AUD"));
    }

    [Test]
    public void PostInternalTransfer_RejectsAnAmountInAnotherCurrency()
    {
        Should.Throw<DomainException>(() => Transfer(10m, amountCurrency: "AUD"));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void PostInternalTransfer_RejectsANonPositiveAmount(decimal amount)
    {
        Should.Throw<DomainException>(() => Transfer(amount));
    }

    [Test]
    public void PostInternalTransfer_RejectsANegativeFromBalance()
    {
        Should.Throw<DomainException>(() => Transfer(500.01m, fromCashSum: 500m));
    }

    [Test]
    public void PostInternalTransfer_AllowsEmptyingTheFromAccount()
    {
        Transfer(500m, fromCashSum: 500m).CashLegs.Count.ShouldBe(2);
    }

    [Test]
    public void Reverse_PutsTheOppositeLegOnEachAccount()
    {
        var original = Transfer(500m);
        original.Id = 7;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 500m, [ToAccountId] = 500m });

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.OriginalTransactionId.ShouldBe(7);
        reversal.CashLegs.Single(leg => leg.AccountId == FromAccountId).Amount.ShouldBe(500m);
        reversal.CashLegs.Single(leg => leg.AccountId == ToAccountId).Amount.ShouldBe(-500m);
    }

    [Test]
    public void Reverse_RejectsWhenTheToAccountSpentTheCash()
    {
        var original = Transfer(500m);
        original.Id = 7;

        Should.Throw<DomainException>(() => original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 500m, [ToAccountId] = 499m }));
    }

    private static Transaction Transfer(
        decimal amount,
        int toAccountId = ToAccountId,
        string toCurrency = "NZD",
        string amountCurrency = "NZD",
        decimal fromCashSum = 1000m) =>
        Transaction.PostInternalTransfer(
            tenantId: 1,
            fromAccountId: FromAccountId,
            fromAccountCurrency: "NZD",
            toAccountId: toAccountId,
            toAccountCurrency: toCurrency,
            amount: Money.Create(amount, amountCurrency),
            bookedAt: BookedAt,
            memo: "Move to savings",
            reference: null,
            fromCurrentCashSum: fromCashSum);
}
