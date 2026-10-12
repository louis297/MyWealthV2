using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class SplitTests
{
    private static readonly DateTimeOffset BookedAt = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(13));

    [TestCase(AccountType.Brokerage)]
    [TestCase(AccountType.Other)]
    public void PostSplit_StoresSignedDeltaAndZeroCostWithoutReleasingTheHoldingCost(AccountType accountType)
    {
        var transaction = Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            type: TransactionType.Split,
            instrumentId: 11,
            newQuantity: 200m,
            quoteCurrency: "usd",
            bookedAt: BookedAt,
            memo: "  Two for one  ",
            reference: "  split-1  ",
            currentQuantity: 100m,
            currentCost: 2500m);

        transaction.TenantId.ShouldBe(1);
        transaction.AccountId.ShouldBe(2);
        transaction.Type.ShouldBe(TransactionType.Split);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("Two for one");
        transaction.Reference.ShouldBe("split-1");
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.CashLeg.ShouldBeNull();
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();

        var leg = transaction.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(100m);
        leg.CostAmount.ShouldBe(0m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [TestCase(TransactionType.Bonus)]
    [TestCase(TransactionType.Scrip)]
    public void PostSplit_BonusAndScripUseTheSameBookRule(TransactionType type)
    {
        var transaction = Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: type,
            instrumentId: 11,
            newQuantity: 115m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 800m);

        transaction.Type.ShouldBe(type);
        transaction.CashLeg.ShouldBeNull();
        var leg = transaction.SecurityLegs.Single();
        leg.Quantity.ShouldBe(15m);
        leg.CostAmount.ShouldBe(0m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void PostSplit_SmallerQuantityStoresANegativeDeltaAndZeroCost()
    {
        var transaction = Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: TransactionType.Split,
            instrumentId: 11,
            newQuantity: 50m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 2500m);

        transaction.CashLeg.ShouldBeNull();
        var leg = transaction.SecurityLegs.Single();
        leg.Quantity.ShouldBe(-50m);
        leg.CostAmount.ShouldBe(0m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void PostSplit_RejectsANewQuantityEqualToTheCurrentQuantity()
    {
        Should.Throw<DomainException>(() => Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: TransactionType.Split,
            instrumentId: 11,
            newQuantity: 100m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 2500m));
    }

    [TestCase(0, 10)]
    [TestCase(-1, 10)]
    [TestCase(100, 0)]
    [TestCase(100, -5)]
    public void PostSplit_RejectsWhenThereIsNoHoldingOrTheNewQuantityIsNotPositive(
        decimal currentQuantity,
        decimal newQuantity)
    {
        Should.Throw<DomainException>(() => Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: TransactionType.Bonus,
            instrumentId: 11,
            newQuantity: newQuantity,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: currentQuantity,
            currentCost: 2500m));
    }

    [TestCase(AccountType.Bank)]
    [TestCase(AccountType.Cash)]
    public void PostSplit_RejectsBankAndCash(AccountType accountType)
    {
        Should.Throw<DomainException>(() => Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            type: TransactionType.Scrip,
            instrumentId: 11,
            newQuantity: 200m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 2500m));
    }

    [Test]
    public void Reverse_CopiesTheOppositeSplitQuantityAndLeavesTheOriginalUntouched()
    {
        var original = Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: TransactionType.Split,
            instrumentId: 11,
            newQuantity: 200m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 2500m);
        var originalQuantity = original.SecurityLegs.Single().Quantity;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            currentCashSum: 0m,
            currentQuantityByInstrument: new Dictionary<int, decimal> { [11] = 200m });

        original.Type.ShouldBe(TransactionType.Split);
        original.CashLeg.ShouldBeNull();
        original.SecurityLegs.Single().Quantity.ShouldBe(originalQuantity);
        original.SecurityLegs.Single().CostAmount.ShouldBe(0m);

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.CashLeg.ShouldBeNull();
        reversal.OriginalTransactionId.ShouldBe(original.Id);
        reversal.ReversedByTransactionId.ShouldBeNull();
        reversal.DomainEvents.OfType<TransactionReversed>().ShouldHaveSingleItem();
        var leg = reversal.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(-originalQuantity);
        leg.CostAmount.ShouldBe(0m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void Reverse_RejectsASplitThatWouldLeaveNoQuantityAndLeavesTheOriginal()
    {
        var original = Transaction.PostSplit(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            type: TransactionType.Split,
            instrumentId: 11,
            newQuantity: 200m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 100m,
            currentCost: 2500m);
        var originalQuantity = original.SecurityLegs.Single().Quantity;

        Should.Throw<DomainException>(() => original.Reverse(
            BookedAt.AddDays(1),
            currentCashSum: 0m,
            currentQuantityByInstrument: new Dictionary<int, decimal> { [11] = 100m }));

        original.Type.ShouldBe(TransactionType.Split);
        original.SecurityLegs.Single().Quantity.ShouldBe(originalQuantity);
        original.SecurityLegs.Single().CostAmount.ShouldBe(0m);
    }
}
