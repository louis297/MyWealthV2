using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>FX conversions §4.1, §5, R11, R13, R14, R17: the FxConversion factory and its reversal.</summary>
public class FxConversionTests
{
    private const int FromAccountId = 41;
    private const int ToAccountId = 42;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(13));
    private static readonly DateOnly AsOf = new(2026, 11, 9);

    [Test]
    public void PostFxConversion_FixedSell_ComputesTheBuyAmountAndBuildsTwoLegs()
    {
        var transaction = Convert(Money.Create(1000.00m, "NZD"));

        transaction.Type.ShouldBe(TransactionType.FxConversion);
        transaction.TenantId.ShouldBe(1);
        transaction.BookedAt.ShouldBe(BookedAt);
        transaction.Memo.ShouldBe("USD for US ETF");
        transaction.PublicId.ShouldNotBe(Guid.Empty);
        transaction.SecurityLegs.ShouldBeEmpty();
        transaction.CashLegs.Count.ShouldBe(2);
        var from = transaction.CashLegs.Single(leg => leg.AccountId == FromAccountId);
        var to = transaction.CashLegs.Single(leg => leg.AccountId == ToAccountId);
        from.Amount.ShouldBe(-1000.00m);
        from.Currency.ShouldBe("NZD");
        to.Amount.ShouldBe(591.20m);
        to.Currency.ShouldBe("USD");
        transaction.DomainEvents.OfType<TransactionPosted>().ShouldHaveSingleItem();
    }

    [Test]
    public void PostFxConversion_FixedBuy_ComputesTheSellAmountRoundedAwayFromZero()
    {
        var transaction = Convert(Money.Create(500.00m, "USD"));

        transaction.CashLegs.Single(leg => leg.AccountId == FromAccountId).Amount.ShouldBe(-845.74m);
        transaction.CashLegs.Single(leg => leg.AccountId == ToAccountId).Amount.ShouldBe(500.00m);
    }

    [Test]
    public void PostFxConversion_FixedSell_RoundsToZeroDecimalPlacesForJpy()
    {
        var transaction = Convert(
            Money.Create(10.00m, "NZD"),
            toCurrency: "JPY",
            rate: Rate("NZD", "JPY", 90.45000000m),
            computedDecimalPlaces: 0);

        // 10.00 × 90.45 = 904.5 → 905 (AwayFromZero)
        transaction.CashLegs.Single(leg => leg.AccountId == ToAccountId).Amount.ShouldBe(905m);
    }

    [Test]
    public void PostFxConversion_FixedBuy_RoundsTheSellSideForJpyFrom()
    {
        var transaction = Convert(
            Money.Create(10.00m, "NZD"),
            fromCurrency: "JPY",
            toCurrency: "NZD",
            rate: Rate("JPY", "NZD", 0.01100000m),
            computedDecimalPlaces: 0,
            fromCashSum: 100000m);

        // 10.00 ÷ 0.011 = 909.09… → 909
        transaction.CashLegs.Single(leg => leg.AccountId == FromAccountId).Amount.ShouldBe(-909m);
        transaction.CashLegs.Single(leg => leg.AccountId == ToAccountId).Amount.ShouldBe(10.00m);
    }

    [Test]
    public void PostFxConversion_StoresTheAppliedRateAsTheFxDetail()
    {
        var transaction = Convert(Money.Create(1000.00m, "NZD"));

        transaction.FxDetail.ShouldNotBeNull();
        transaction.FxDetail.BaseCurrency.ShouldBe("NZD");
        transaction.FxDetail.QuoteCurrency.ShouldBe("USD");
        transaction.FxDetail.Rate.ShouldBe(0.59120000m);
        transaction.FxDetail.RateAsOfDate.ShouldBe(AsOf);
        transaction.FxDetail.RateSource.ShouldBe("Frankfurter");
    }

    [Test]
    public void PostFxConversion_RejectsTheSameAccount()
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(10m, "NZD"), toAccountId: FromAccountId));
    }

    [Test]
    public void PostFxConversion_RejectsTheSameCurrency()
    {
        Should.Throw<DomainException>(() => Convert(
            Money.Create(10m, "NZD"),
            toCurrency: "NZD"));
    }

    [Test]
    public void PostFxConversion_RejectsARateForAnotherPair()
    {
        Should.Throw<DomainException>(() => Convert(
            Money.Create(10m, "NZD"),
            rate: Rate("USD", "NZD", 1.69000000m)));
    }

    [Test]
    public void PostFxConversion_RejectsAnAmountInAThirdCurrency()
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(10m, "AUD")));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void PostFxConversion_RejectsANonPositiveAmount(decimal amount)
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(amount, "NZD")));
    }

    [Test]
    public void PostFxConversion_RejectsAComputedAmountOfZero()
    {
        // 0.01 NZD × 0.1 = 0.001 → 0.00 USD
        Should.Throw<DomainException>(() => Convert(
            Money.Create(0.01m, "NZD"),
            rate: Rate("NZD", "USD", 0.10000000m)));
    }

    [Test]
    public void PostFxConversion_RejectsANegativeFromBalance()
    {
        Should.Throw<DomainException>(() => Convert(Money.Create(1000.01m, "NZD"), fromCashSum: 1000m));
    }

    [TestCase(0)]
    [TestCase(-0.5)]
    public void AppliedFxRate_RejectsANonPositiveRate(decimal rate)
    {
        Should.Throw<DomainException>(() => AppliedFxRate.Create("NZD", "USD", rate, AsOf, "Frankfurter"));
    }

    [Test]
    public void AppliedFxRate_RejectsEqualCurrencies()
    {
        Should.Throw<DomainException>(() => AppliedFxRate.Create("NZD", "NZD", 1m, AsOf, "Frankfurter"));
    }

    [Test]
    public void AppliedFxRate_RejectsAnEmptySource()
    {
        Should.Throw<DomainException>(() => AppliedFxRate.Create("NZD", "USD", 0.5m, AsOf, " "));
    }

    [Test]
    public void Reverse_PutsTheOriginalAmountsBackAndCopiesTheFxDetail()
    {
        var original = Convert(Money.Create(1000.00m, "NZD"));
        original.Id = 7;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 0m, [ToAccountId] = 591.20m });

        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.OriginalTransactionId.ShouldBe(7);
        reversal.CashLegs.Single(leg => leg.AccountId == FromAccountId).Amount.ShouldBe(1000.00m);
        reversal.CashLegs.Single(leg => leg.AccountId == FromAccountId).Currency.ShouldBe("NZD");
        reversal.CashLegs.Single(leg => leg.AccountId == ToAccountId).Amount.ShouldBe(-591.20m);
        reversal.CashLegs.Single(leg => leg.AccountId == ToAccountId).Currency.ShouldBe("USD");
        reversal.FxDetail.ShouldNotBeNull();
        reversal.FxDetail.BaseCurrency.ShouldBe("NZD");
        reversal.FxDetail.QuoteCurrency.ShouldBe("USD");
        reversal.FxDetail.Rate.ShouldBe(0.59120000m);
        reversal.FxDetail.RateAsOfDate.ShouldBe(AsOf);
        reversal.FxDetail.RateSource.ShouldBe("Frankfurter");
        reversal.FxDetail.ShouldNotBeSameAs(original.FxDetail);
    }

    [Test]
    public void Reverse_RejectsWhenTheToAccountSpentTheCash()
    {
        var original = Convert(Money.Create(1000.00m, "NZD"));
        original.Id = 7;

        Should.Throw<DomainException>(() => original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 0m, [ToAccountId] = 591.19m }));
    }

    [Test]
    public void Reverse_OfAnotherType_HasNoFxDetail()
    {
        var original = Transaction.PostInternalTransfer(
            1, FromAccountId, "NZD", ToAccountId, "NZD", Money.Create(10m, "NZD"), BookedAt, null, null, 100m);
        original.Id = 8;

        var reversal = original.Reverse(
            BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [FromAccountId] = 90m, [ToAccountId] = 10m });

        reversal.FxDetail.ShouldBeNull();
    }

    private static AppliedFxRate Rate(string baseCurrency, string quoteCurrency, decimal rate) =>
        AppliedFxRate.Create(baseCurrency, quoteCurrency, rate, AsOf, "Frankfurter");

    private static Transaction Convert(
        Money fixedAmount,
        int toAccountId = ToAccountId,
        string fromCurrency = "NZD",
        string toCurrency = "USD",
        AppliedFxRate? rate = null,
        int computedDecimalPlaces = 2,
        decimal fromCashSum = 5000m) =>
        Transaction.PostFxConversion(
            tenantId: 1,
            fromAccountId: FromAccountId,
            fromAccountCurrency: fromCurrency,
            toAccountId: toAccountId,
            toAccountCurrency: toCurrency,
            rate: rate ?? Rate("NZD", "USD", 0.59120000m),
            fixedAmount: fixedAmount,
            computedDecimalPlaces: computedDecimalPlaces,
            bookedAt: BookedAt,
            memo: "USD for US ETF",
            reference: null,
            fromCurrentCashSum: fromCashSum);
}
