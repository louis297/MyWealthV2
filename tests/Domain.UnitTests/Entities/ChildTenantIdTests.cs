using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Tenant composite keys B1: every child row carries its parent's TenantId.</summary>
public class ChildTenantIdTests
{
    private const int TenantId = 7;
    private const int AccountId = 42;
    private const int OtherAccountId = 43;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(13));

    [Test]
    public void Post_PutsTheTenantOnTheCashLeg()
    {
        var transaction = Transaction.Post(
            tenantId: TenantId,
            accountId: AccountId,
            type: TransactionType.TransferIn,
            amount: 100m,
            accountCurrency: "NZD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            existingTransactionCount: 0);

        transaction.CashLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PostInternalTransfer_PutsTheTenantOnBothCashLegs()
    {
        var transaction = Transaction.PostInternalTransfer(
            tenantId: TenantId,
            fromAccountId: AccountId,
            fromAccountCurrency: "NZD",
            toAccountId: OtherAccountId,
            toAccountCurrency: "NZD",
            amount: Money.Create(10m, "NZD"),
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            fromCurrentCashSum: 100m);

        transaction.CashLegs.Select(leg => leg.TenantId).ShouldBe([TenantId, TenantId]);
    }

    [Test]
    public void PostFxConversion_PutsTheTenantOnBothCashLegsAndTheFxDetail()
    {
        var transaction = FxConversion();

        transaction.CashLegs.Select(leg => leg.TenantId).ShouldBe([TenantId, TenantId]);
        transaction.FxDetail!.TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PostOpeningHoldings_PutsTheTenantOnEverySecurityLeg()
    {
        var transaction = Transaction.PostOpeningHoldings(
            tenantId: TenantId,
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

        transaction.SecurityLegs.Select(leg => leg.TenantId).ShouldBe([TenantId, TenantId]);
    }

    [Test]
    public void PostBuy_PutsTheTenantOnTheCashAndSecurityLegs()
    {
        var transaction = Buy();

        transaction.CashLegs.Single().TenantId.ShouldBe(TenantId);
        transaction.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PostSell_PutsTheTenantOnTheCashAndSecurityLegs()
    {
        var transaction = Transaction.PostSell(
            tenantId: TenantId,
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

        transaction.CashLegs.Single().TenantId.ShouldBe(TenantId);
        transaction.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PostStockInAndStockOut_PutTheTenantOnTheSecurityLeg()
    {
        var stockIn = Transaction.PostStockIn(
            tenantId: TenantId,
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
            tenantId: TenantId,
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

        stockIn.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
        stockOut.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [TestCase(TransactionType.Split)]
    [TestCase(TransactionType.Bonus)]
    [TestCase(TransactionType.Scrip)]
    public void PostSplit_PutsTheTenantOnTheSecurityLeg(TransactionType type)
    {
        var transaction = Transaction.PostSplit(
            tenantId: TenantId,
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

        transaction.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void Reverse_PutsTheTenantOnEveryOppositeLeg()
    {
        var original = Buy();
        original.Id = 9;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal> { [AccountId] = 980m });

        reversal.CashLegs.Single().TenantId.ShouldBe(TenantId);
        reversal.SecurityLegs.Single().TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void Reverse_PutsTheTenantOnTheCopiedFxDetail()
    {
        var original = FxConversion();
        original.Id = 9;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            currentCashSumByAccount: new Dictionary<int, decimal>
            {
                [AccountId] = 0m,
                [OtherAccountId] = 1000m
            });

        reversal.CashLegs.Select(leg => leg.TenantId).ShouldBe([TenantId, TenantId]);
        reversal.FxDetail!.TenantId.ShouldBe(TenantId);
    }

    [Test]
    public void PortfolioCreate_PutsTheTenantOnEveryLine()
    {
        var portfolio = Portfolio.Create(TenantId, "Balanced", Nzd(), [(10, 60m), (11, 40m)]);

        portfolio.Lines.Select(line => line.TenantId).ShouldBe([TenantId, TenantId]);
    }

    [Test]
    public void PortfolioReplaceLines_PutsTheTenantOnEveryNewLine()
    {
        var portfolio = Portfolio.Create(TenantId, "Balanced", Nzd(), [(10, 60m), (11, 40m)]);

        portfolio.ReplaceLines([(10, 50m), (12, 50m)]);

        portfolio.Lines.Select(line => line.TenantId).ShouldBe([TenantId, TenantId]);
    }

    private static Transaction Buy() =>
        Transaction.PostBuy(
            tenantId: TenantId,
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

    private static Transaction FxConversion() =>
        Transaction.PostFxConversion(
            tenantId: TenantId,
            fromAccountId: AccountId,
            fromAccountCurrency: "NZD",
            toAccountId: OtherAccountId,
            toAccountCurrency: "USD",
            rate: AppliedFxRate.Create("NZD", "USD", 0.5m, new DateOnly(2026, 11, 12), "Frankfurter"),
            fixedAmount: Money.Create(100m, "NZD"),
            computedDecimalPlaces: 2,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            fromCurrentCashSum: 1000m);

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}
