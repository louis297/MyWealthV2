using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class OpeningHoldingsTests
{
    private static readonly DateTimeOffset BookedAt = new(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(12));

    [TestCase(AccountType.Brokerage)]
    [TestCase(AccountType.Other)]
    public void PostOpeningHoldings_StoresSecurityLegsAndNoCashLeg(AccountType accountType)
    {
        var transaction = Post(accountType, (11, 100m, 1500.00m, "USD"));

        transaction.TenantId.ShouldBe(1);
        transaction.AccountId.ShouldBe(2);
        transaction.Type.ShouldBe(TransactionType.OpeningHoldings);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.CashLeg.ShouldBeNull();
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();

        TransactionSecurityLeg leg = transaction.SecurityLegs.Single();
        leg.InstrumentId.ShouldBe(11);
        leg.Quantity.ShouldBe(100m);
        leg.CostAmount.ShouldBe(1500.00m);
        leg.CostCurrency.ShouldBe("USD");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PostOpeningHoldings_RejectsQuantityThatIsNotPositive(decimal quantity)
    {
        Should.Throw<DomainException>(() => Post(AccountType.Brokerage, (11, quantity, 10m, "USD")));
    }

    [Test]
    public void PostOpeningHoldings_AllowsZeroCostAndRejectsNegativeCost()
    {
        var transaction = Post(AccountType.Brokerage, (11, 1m, 0m, "USD"));
        TransactionSecurityLeg leg = transaction.SecurityLegs.Single();
        leg.CostAmount.ShouldBe(0m);

        Should.Throw<DomainException>(() => Post(AccountType.Brokerage, (11, 1m, -0.01m, "USD")));
    }

    [Test]
    public void Post_RejectsOpeningHoldingsAsACashMovement()
    {
        Should.Throw<DomainException>(() => Transaction.Post(
            tenantId: 1,
            accountId: 2,
            type: TransactionType.OpeningHoldings,
            amount: 100m,
            accountCurrency: "NZD",
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            currentCashSum: 0m,
            existingTransactionCount: 0));
    }

    [TestCase(AccountType.Bank)]
    [TestCase(AccountType.Cash)]
    public void PostOpeningHoldings_RejectsBankAndCash(AccountType accountType)
    {
        Should.Throw<DomainException>(() => Post(accountType, (11, 1m, 10m, "USD")));
    }

    [Test]
    public void PostOpeningHoldings_RejectsTheSameInstrumentTwiceOnOneHeader()
    {
        Should.Throw<DomainException>(() => Post(
            AccountType.Brokerage,
            (11, 1m, 10m, "USD"),
            (11, 2m, 0m, "USD")));

        var transaction = Post(
            AccountType.Brokerage,
            (11, 1m, 10m, "USD"),
            (12, 2m, 0m, "USD"));

        transaction.CashLeg.ShouldBeNull();
        transaction.SecurityLegs.Select(leg => leg.InstrumentId).ShouldBe([11, 12]);
    }

    private static Transaction Post(
        AccountType accountType,
        params (int InstrumentId, decimal Quantity, decimal Cost, string QuoteCurrency)[] legs) =>
        Transaction.PostOpeningHoldings(
            tenantId: 1,
            accountId: 2,
            accountType: accountType,
            bookedAt: BookedAt,
            memo: null,
            reference: null,
            legs: legs);
}
