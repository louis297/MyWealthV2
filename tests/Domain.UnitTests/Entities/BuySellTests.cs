using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
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

    [Test]
    public void PostStockIn_StoresClientCostWithoutACashLeg()
    {
        var transaction = Transaction.PostStockIn(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 20m,
            cost: 300.00m,
            quoteCurrency: "usd",
            bookedAt: BookedAt,
            memo: null,
            reference: null);

        transaction.Type.ShouldBe(TransactionType.StockIn);
        transaction.CashLeg.ShouldBeNull();
        var leg = transaction.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(20m);
        leg.CostAmount.ShouldBe(300.00m);
        leg.CostCurrency.ShouldBe("USD");
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();

        var zero = Transaction.PostStockIn(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Other,
            instrumentId: 12,
            quantity: 1m,
            cost: 0m,
            quoteCurrency: "NZD",
            bookedAt: BookedAt,
            memo: null,
            reference: null);
        zero.CashLeg.ShouldBeNull();
        zero.SecurityLegs.Single().CostAmount.ShouldBe(0m);

        Should.Throw<DomainException>(() => Transaction.PostStockIn(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 1m,
            cost: -0.01m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null));
    }

    [Test]
    public void PostStockOut_ReleasesAverageCostWithoutCashAndRejectsOversell()
    {
        var partial = Transaction.PostStockOut(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 1m,
            quoteCurrency: "usd",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 3m,
            currentCost: 10m);

        partial.Type.ShouldBe(TransactionType.StockOut);
        partial.CashLeg.ShouldBeNull();
        partial.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
        var partialLeg = partial.SecurityLegs.Single();
        partialLeg.Quantity.ShouldBe(-1m);
        partialLeg.CostAmount.ShouldBe(-3.3333m);
        partialLeg.CostCurrency.ShouldBe("USD");

        var full = Transaction.PostStockOut(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Other,
            instrumentId: 11,
            quantity: 2m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 2m,
            currentCost: 500.0001m);
        full.CashLeg.ShouldBeNull();
        full.SecurityLegs.Single().Quantity.ShouldBe(-2m);
        full.SecurityLegs.Single().CostAmount.ShouldBe(-500.0001m);

        Should.Throw<DomainException>(() => Transaction.PostStockOut(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 1m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 0m,
            currentCost: 0m));
        Should.Throw<DomainException>(() => Transaction.PostStockOut(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 4m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 3m,
            currentCost: 10m));
        Should.Throw<DomainException>(() => Transaction.PostSell(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            accountCurrency: "USD",
            instrumentId: 11,
            quantity: 4m,
            price: Money.Create(15m, "USD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            currentQuantity: 3m,
            currentCost: 10m));
    }

    [TestCase(AccountType.Bank)]
    [TestCase(AccountType.Cash)]
    [TestCase(AccountType.Property)]
    [TestCase(AccountType.Credit)]
    public void SecurityPosts_RejectBankCashPropertyAndCredit(AccountType accountType)
    {
        Should.Throw<DomainException>(() => Transaction.PostBuy(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            accountCurrency: "USD",
            instrumentId: 11,
            quantity: 1m,
            price: Money.Create(10m, "USD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 100m));
        Should.Throw<DomainException>(() => Transaction.PostSell(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            accountCurrency: "USD",
            instrumentId: 11,
            quantity: 1m,
            price: Money.Create(10m, "USD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            currentQuantity: 5m,
            currentCost: 50m));
        Should.Throw<DomainException>(() => Transaction.PostStockIn(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            instrumentId: 11,
            quantity: 1m,
            cost: 10m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null));
        Should.Throw<DomainException>(() => Transaction.PostStockOut(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            instrumentId: 11,
            quantity: 1m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 5m,
            currentCost: 50m));
    }

    [TestCase(0)]
    [TestCase(-0.01)]
    public void PostBuyAndPostSell_RejectAnEmptyPrice(decimal price)
    {
        Should.Throw<DomainException>(() => PostBuy(
            quantity: 1m,
            price: price,
            priceCurrency: "USD",
            accountCurrency: "USD",
            fxRate: 1m));
        Should.Throw<DomainException>(() => Transaction.PostSell(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            accountCurrency: "USD",
            instrumentId: 11,
            quantity: 1m,
            price: Money.Create(price, "USD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            currentQuantity: 5m,
            currentCost: 50m));
    }

    [Test]
    public void Reverse_BuyCopiesOppositeCashAndSecurityAndLeavesTheOriginal()
    {
        var original = PostBuy(quantity: 2m, price: 250m, priceCurrency: "USD", accountCurrency: "USD", fxRate: 1m);
        var originalCash = original.CashLeg!.Amount;
        var originalLeg = original.SecurityLegs.Single();
        var originalQuantity = originalLeg.Quantity;
        var originalCost = originalLeg.CostAmount;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            currentCashSum: 500m,
            currentQuantityByInstrument: new Dictionary<int, decimal> { [11] = 2m });

        original.Type.ShouldBe(TransactionType.Buy);
        original.CashLeg.Amount.ShouldBe(originalCash);
        original.SecurityLegs.Single().Quantity.ShouldBe(originalQuantity);
        original.SecurityLegs.Single().CostAmount.ShouldBe(originalCost);

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.OriginalTransactionId.ShouldBe(original.Id);
        reversal.CashLeg.ShouldNotBeNull();
        reversal.CashLeg!.Amount.ShouldBe(-originalCash);
        reversal.CashLeg.Currency.ShouldBe("USD");
        var leg = reversal.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(-originalQuantity);
        leg.CostAmount.ShouldBe(-originalCost);
        leg.CostCurrency.ShouldBe("USD");
        reversal.DomainEvents.OfType<TransactionReversed>().ShouldHaveSingleItem();
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
