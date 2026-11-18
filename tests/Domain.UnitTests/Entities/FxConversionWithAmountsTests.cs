using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>External caller numbers §5, R17, R18: <c>PostFxConversionWithAmounts</c> and its reversal.</summary>
public class FxConversionWithAmountsTests
{
    private const int FromAccountId = 41;
    private const int ToAccountId = 42;

    // 2026-11-10 09:00 +13:00 is 2026-11-09 20:00 UTC.
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(13));

    [Test]
    public void BothAmounts_BookTheLegsAsEnteredAndDeriveTheRate()
    {
        var transaction = Convert(Money.Create(845.74m, "NZD"), Money.Create(500.00m, "USD"));

        transaction.Type.ShouldBe(TransactionType.FxConversion);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("Bank conversion");
        transaction.CashLegs.Select(leg => (leg.AccountId, leg.Amount, leg.Currency))
            .ShouldBe([(FromAccountId, -845.74m, "NZD"), (ToAccountId, 500.00m, "USD")]);
        var fx = transaction.FxDetail.ShouldNotBeNull();
        (fx.BaseCurrency, fx.QuoteCurrency, fx.Rate, fx.RateAsOfDate, fx.RateSource)
            .ShouldBe(("NZD", "USD", 0.59119824m, new DateOnly(2026, 11, 9), "Caller"));
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }

    [Test]
    public void ZeroDecimalCurrency_DerivesTheRateToEightDecimals()
    {
        var transaction = Convert(Money.Create(100000m, "JPY"), Money.Create(612.40m, "USD"), fromCurrency: "JPY", fromCashSum: 100000m);

        transaction.FxDetail!.Rate.ShouldBe(0.00612400m);
    }

    [Test]
    public void SameAccountSameCurrencyOrDifferentCustodyModes_Throw()
    {
        Should.Throw<DomainException>(() =>
            Convert(Money.Create(10m, "NZD"), Money.Create(5m, "USD"), toAccountId: FromAccountId));
        Should.Throw<DomainException>(() =>
            Convert(Money.Create(10m, "NZD"), Money.Create(10m, "NZD"), toCurrency: "NZD"));
        Should.Throw<DomainException>(() =>
                Convert(Money.Create(10m, "NZD"), Money.Create(5m, "USD"), toCustodyMode: CustodyMode.Custodial))
            .Message.ShouldBe(Transaction.DifferentCustodyModesMessage);
    }

    [Test]
    public void AmountCurrencyNotTheAccountCurrency_Throws()
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(10m, "AUD"), Money.Create(5m, "USD")));
        Should.Throw<DomainException>(() => Convert(Money.Create(10m, "NZD"), Money.Create(5m, "AUD")));
    }

    [TestCase(0, 5)]
    [TestCase(10, 0)]
    [TestCase(-10, 5)]
    [TestCase(10, -5)]
    public void AmountNotPositive_Throws(decimal sell, decimal buy)
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(sell, "NZD"), Money.Create(buy, "USD")));
    }

    [Test]
    public void RateThatRoundsToZeroOrDoesNotFitTheColumn_Throws()
    {
        Should.Throw<DomainException>(() =>
            Convert(Money.Create(9_000_000_000m, "NZD"), Money.Create(0.01m, "USD"), fromCashSum: 10_000_000_000m));
        Should.Throw<DomainException>(() =>
            Convert(Money.Create(0.01m, "NZD"), Money.Create(1_000_000_000m, "USD")));
    }

    [Test]
    public void FromCashBelowTheSellAmount_Throws()
    {
        Should.Throw<DomainException>(() =>
                Convert(Money.Create(845.74m, "NZD"), Money.Create(500.00m, "USD"), fromCashSum: 845.73m))
            .Message.ShouldBe("Cash balance cannot become negative.");
    }

    [Test]
    public void Reverse_CopiesTheAmountsAndTheCallerFxDetail()
    {
        var original = Convert(Money.Create(845.74m, "NZD"), Money.Create(500.00m, "USD"));
        original.Id = 9;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 0m, [ToAccountId] = 500.00m });

        reversal.CashLegs.Select(leg => (leg.AccountId, leg.Amount))
            .ShouldBe([(FromAccountId, 845.74m), (ToAccountId, -500.00m)], ignoreOrder: true);
        var fx = reversal.FxDetail.ShouldNotBeNull();
        (fx.Rate, fx.RateAsOfDate, fx.RateSource).ShouldBe((0.59119824m, new DateOnly(2026, 11, 9), "Caller"));
    }

    private static Transaction Convert(
        Money sell,
        Money buy,
        int toAccountId = ToAccountId,
        string fromCurrency = "NZD",
        string toCurrency = "USD",
        decimal fromCashSum = 5000m,
        CustodyMode fromCustodyMode = CustodyMode.External,
        CustodyMode toCustodyMode = CustodyMode.External) =>
        Transaction.PostFxConversionWithAmounts(
            tenantId: 1,
            fromAccountId: FromAccountId,
            fromAccountCurrency: fromCurrency,
            fromCustodyMode: fromCustodyMode,
            toAccountId: toAccountId,
            toAccountCurrency: toCurrency,
            toCustodyMode: toCustodyMode,
            sell: sell,
            buy: buy,
            bookedAt: BookedAt,
            memo: "Bank conversion",
            reference: null,
            fromCurrentCashSum: fromCashSum);
}
