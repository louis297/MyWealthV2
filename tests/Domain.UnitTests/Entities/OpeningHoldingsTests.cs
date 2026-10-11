using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
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

    [Test]
    public void PostOpeningHoldings_RejectsAnEmptyLegList()
    {
        Should.Throw<DomainException>(() => Post(AccountType.Brokerage));
    }

    [TestCase(AccountType.Property)]
    [TestCase(AccountType.Credit)]
    public void PostOpeningHoldings_RejectsPropertyAndCredit(AccountType accountType)
    {
        Should.Throw<DomainException>(() => Post(accountType, (11, 1m, 10m, "USD")));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void OpeningSecurityLeg_RejectsANonPositiveInstrumentId(int instrumentId)
    {
        Should.Throw<DomainException>(() => OpeningSecurityLeg.Create(instrumentId, 1m, 10m, "USD"));
    }

    [Test]
    public void OpeningSecurityLeg_StoresQuoteCurrencyInUpperCaseAndRejectsAShortOrEmptyCode()
    {
        OpeningSecurityLeg leg = OpeningSecurityLeg.Create(11, 1m, 10m, "usd");
        leg.QuoteCurrency.ShouldBe("USD");

        Transaction posted = Post(AccountType.Brokerage, (11, 1m, 10m, "usd"));
        posted.SecurityLegs.Single().CostCurrency.ShouldBe("USD");

        Should.Throw<DomainException>(() => OpeningSecurityLeg.Create(11, 1m, 10m, "US"));
        Should.Throw<DomainException>(() => OpeningSecurityLeg.Create(11, 1m, 10m, ""));
    }

    [Test]
    public void PostOpeningHoldings_StoresATrimmedMemoOf200CharactersAndClearsBlankReference()
    {
        var memo = new string('m', 200);

        var transaction = Transaction.PostOpeningHoldings(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            bookedAt: BookedAt,
            memo: $"  {memo}  ",
            reference: "   ",
            securityLegs: [OpeningSecurityLeg.Create(11, 1m, 10m, "USD")]);

        transaction.Memo.ShouldBe(memo);
        transaction.Reference.ShouldBeNull();

        Should.Throw<DomainException>(() => Transaction.PostOpeningHoldings(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            bookedAt: BookedAt,
            memo: new string('m', 201),
            reference: null,
            securityLegs: [OpeningSecurityLeg.Create(11, 1m, 10m, "USD")]));
    }

    [Test]
    public void Reverse_CopiesOppositeSecurityLegsAndLeavesTheOriginalUntouched()
    {
        var original = Transaction.PostOpeningHoldings(
            tenantId: 1,
            accountId: 2,
            accountType: AccountType.Brokerage,
            bookedAt: BookedAt,
            memo: "Open VTI",
            reference: "ref",
            securityLegs:
            [
                OpeningSecurityLeg.Create(11, 100m, 1500.00m, "USD"),
                OpeningSecurityLeg.Create(12, 2m, 0m, "USD")
            ]);
        original.Id = 7;
        original.ClearDomainEvents();
        var reverseAt = BookedAt.AddDays(1);

        var reversal = original.Reverse(reverseAt, currentCashSum: 0m);

        original.Type.ShouldBe(TransactionType.OpeningHoldings);
        original.Memo.ShouldBe("Open VTI");
        original.Reference.ShouldBe("ref");
        original.CashLeg.ShouldBeNull();
        original.DomainEvents.ShouldBeEmpty();
        original.SecurityLegs.Select(leg => (leg.InstrumentId, leg.Quantity, leg.CostAmount, leg.CostCurrency))
            .ShouldBe([(11, 100m, 1500.00m, "USD"), (12, 2m, 0m, "USD")]);

        reversal.ShouldNotBeSameAs(original);
        reversal.PublicId.ShouldNotBe(original.PublicId);
        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.TenantId.ShouldBe(1);
        reversal.AccountId.ShouldBe(2);
        reversal.BookedAt.ShouldBe(reverseAt);
        reversal.OriginalTransactionId.ShouldBe(7);
        reversal.Memo.ShouldBeNull();
        reversal.Reference.ShouldBeNull();
        reversal.CashLeg.ShouldBeNull();
        reversal.SecurityLegs.Select(leg => (leg.InstrumentId, leg.Quantity, leg.CostAmount, leg.CostCurrency))
            .ShouldBe([(11, -100m, -1500.00m, "USD"), (12, -2m, 0m, "USD")]);
        reversal.DomainEvents.OfType<TransactionReversed>().ShouldHaveSingleItem();
        reversal.DomainEvents.OfType<TransactionPosted>().ShouldBeEmpty();
    }

    [Test]
    public void Reverse_RejectsReversalOfAnOpeningHoldingsReversal()
    {
        var original = Post(AccountType.Brokerage, (11, 100m, 1500m, "USD"));
        original.Id = 7;
        var reversal = original.Reverse(BookedAt.AddDays(1), currentCashSum: 0m);

        Should.Throw<DomainException>(() => reversal.Reverse(BookedAt.AddDays(2), currentCashSum: 0m));
    }

    [Test]
    public void Reverse_RejectsWhenHoldingQuantityWouldBecomeNegative()
    {
        var original = Post(AccountType.Brokerage, (11, 100m, 1500m, "USD"), (12, 2m, 0m, "USD"));
        original.Id = 7;

        Should.Throw<DomainException>(() => original.Reverse(
            BookedAt.AddDays(1),
            currentCashSum: 0m,
            currentQuantityByInstrument: new Dictionary<int, decimal>
            {
                [11] = 100m,
                [12] = 1m
            }));
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
            securityLegs: legs.Select(t => OpeningSecurityLeg.Create(t.InstrumentId, t.Quantity, t.Cost, t.QuoteCurrency)));
}
