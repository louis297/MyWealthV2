using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
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
}
