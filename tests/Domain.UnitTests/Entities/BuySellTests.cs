using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class BuySellTests
{
    private static readonly DateTimeOffset BookedAt = new(2026, 10, 11, 10, 0, 0, TimeSpan.FromHours(13));

    [TestCase(AccountType.Brokerage)]
    [TestCase(AccountType.Other)]
    public void PostBuy_StoresPositiveQuantityServerCostAndNegativeCash(AccountType accountType)
    {
        var transaction = Transaction.PostBuy(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            accountCurrency: "nzd",
            instrumentId: 11,
            quantity: 50m,
            price: Money.Create(15m, "USD"),
            fxRate: 5m / 3m,
            bookedAt: BookedAt,
            memo: "  First buy  ",
            reference: "  ref-1  ",
            currentCashSum: 2000m);

        transaction.TenantId.ShouldBe(1);
        transaction.AccountId.ShouldBe(2);
        transaction.Type.ShouldBe(TransactionType.Buy);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("First buy");
        transaction.Reference.ShouldBe("ref-1");
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();

        transaction.CashLeg.ShouldNotBeNull();
        transaction.CashLeg!.Amount.ShouldBe(-1250.0000m);
        transaction.CashLeg.Currency.ShouldBe("NZD");

        var leg = transaction.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(50m);
        leg.CostAmount.ShouldBe(750.0000m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void PostBuy_SameCurrencyRateOfOneChargesTheQuoteCost()
    {
        var transaction = PostBuy(quantity: 2m, price: 250m, priceCurrency: "USD", accountCurrency: "USD", fxRate: 1m);

        transaction.CashLeg!.Amount.ShouldBe(-500.0000m);
        transaction.CashLeg.Currency.ShouldBe("USD");
        var leg = transaction.SecurityLegs.Single();
        leg.Quantity.ShouldBe(2m);
        leg.CostAmount.ShouldBe(500.0000m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void PostBuy_RoundsCostAndCashAwayFromZero()
    {
        var transaction = PostBuy(quantity: 1m, price: 10.00005m, priceCurrency: "USD", accountCurrency: "USD", fxRate: 1m);

        transaction.SecurityLegs.Single().CostAmount.ShouldBe(10.0001m);
        transaction.CashLeg!.Amount.ShouldBe(-10.0001m);
    }

    [Test]
    public void PostSell_ReleasesAverageCostAndIncreasesCash()
    {
        var partial = Transaction.PostSell(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            accountCurrency: "USD",
            instrumentId: 11,
            quantity: 1m,
            price: Money.Create(15m, "USD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            currentQuantity: 3m,
            currentCost: 10m);

        partial.Type.ShouldBe(TransactionType.Sell);
        partial.CashLeg.ShouldNotBeNull();
        partial.CashLeg!.Amount.ShouldBe(15.0000m);
        partial.CashLeg.Currency.ShouldBe("USD");
        var partialLeg = partial.SecurityLegs.Single();
        partialLeg.Quantity.ShouldBe(-1m);
        partialLeg.CostAmount.ShouldBe(-3.3333m);
        partialLeg.CostCurrency.ShouldBe("USD");
        partial.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();

        var full = Transaction.PostSell(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Other,
            accountCurrency: "NZD",
            instrumentId: 11,
            quantity: 2m,
            price: Money.Create(250m, "USD"),
            fxRate: 1.6m,
            bookedAt: BookedAt,
            memo: null,
            reference: " ",
            currentCashSum: 10m,
            currentQuantity: 2m,
            currentCost: 500.0001m);

        full.Reference.ShouldBeNull();
        full.CashLeg!.Amount.ShouldBe(800.0000m);
        full.CashLeg.Currency.ShouldBe("NZD");
        var fullLeg = full.SecurityLegs.Single();
        fullLeg.Quantity.ShouldBe(-2m);
        fullLeg.CostAmount.ShouldBe(-500.0001m);
        fullLeg.CostCurrency.ShouldBe("USD");
    }

    private static Transaction PostBuy(
        decimal quantity,
        decimal price,
        string priceCurrency,
        string accountCurrency,
        decimal fxRate,
        decimal currentCashSum = 1000m) =>
        Transaction.PostBuy(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            accountCurrency: accountCurrency,
            instrumentId: 11,
            quantity: quantity,
            price: Money.Create(price, priceCurrency),
            fxRate: fxRate,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: currentCashSum);
}
