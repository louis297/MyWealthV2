using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class LegAccountTests
{
    private const int AccountId = 42;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(13));

    [TestCase(TransactionType.TransferIn, 100)]
    [TestCase(TransactionType.TransferOut, -40)]
    [TestCase(TransactionType.Interest, 5)]
    [TestCase(TransactionType.Opening, 100)]
    public void Post_StampsTheAccountOnTheCashLeg(TransactionType type, decimal amount)
    {
        var transaction = Transaction.Post(
            tenantId: 1,
            accountId: AccountId,
            type: type,
            amount: amount,
            accountCurrency: "NZD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 100m,
            existingTransactionCount: 0);

        transaction.CashLegs.Single().AccountId.ShouldBe(AccountId);
    }

    [Test]
    public void PostOpeningHoldings_StampsTheAccountOnEverySecurityLeg()
    {
        var transaction = Transaction.PostOpeningHoldings(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            securityLegs:
            [
                OpeningSecurityLeg.Create(11, 100m, 1500m, "USD"),
                OpeningSecurityLeg.Create(12, 2m, 0m, "USD")
            ]);

        transaction.SecurityLegs.Select(leg => leg.AccountId).ShouldBe([AccountId, AccountId]);
    }

    [Test]
    public void PostBuy_StampsTheAccountOnTheCashAndSecurityLegs()
    {
        var transaction = Transaction.PostBuy(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            accountCurrency: "NZD",
            instrumentId: 11,
            quantity: 2m,
            price: Money.Create(10m, "NZD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 1000m);

        transaction.CashLegs.Single().AccountId.ShouldBe(AccountId);
        transaction.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
    }

    [Test]
    public void PostSell_StampsTheAccountOnTheCashAndSecurityLegs()
    {
        var transaction = Transaction.PostSell(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            accountCurrency: "NZD",
            instrumentId: 11,
            quantity: 1m,
            price: Money.Create(10m, "NZD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            currentQuantity: 2m,
            currentCost: 20m);

        transaction.CashLegs.Single().AccountId.ShouldBe(AccountId);
        transaction.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
    }

    [Test]
    public void PostStockInAndStockOut_StampTheAccountOnTheSecurityLeg()
    {
        var stockIn = Transaction.PostStockIn(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 2m,
            cost: 20m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null);
        var stockOut = Transaction.PostStockOut(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            instrumentId: 11,
            quantity: 1m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 2m,
            currentCost: 20m);

        stockIn.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
        stockOut.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
    }

    [TestCase(TransactionType.Split)]
    [TestCase(TransactionType.Bonus)]
    [TestCase(TransactionType.Scrip)]
    public void PostSplit_StampsTheAccountOnTheSecurityLeg(TransactionType type)
    {
        var transaction = Transaction.PostSplit(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            type: type,
            instrumentId: 11,
            newQuantity: 4m,
            quoteCurrency: "USD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentQuantity: 2m,
            currentCost: 20m);

        transaction.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
    }

    [Test]
    public void Reverse_PutsEachOppositeLegOnTheOriginalLegsAccount()
    {
        var original = Transaction.PostBuy(
            tenantId: 1,
            accountId: AccountId,
            accountType: AccountType.Brokerage,
            accountCurrency: "NZD",
            instrumentId: 11,
            quantity: 2m,
            price: Money.Create(10m, "NZD"),
            fxRate: 1m,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 1000m);
        original.Id = 7;

        var reversal = original.Reverse(BookedAt.AddDays(1), currentCashSum: 980m);

        reversal.CashLegs.Single().AccountId.ShouldBe(AccountId);
        reversal.SecurityLegs.Single().AccountId.ShouldBe(AccountId);
    }
}
