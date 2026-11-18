using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>
/// External caller numbers §5, R8–R10, R12: <c>PostBuy</c> / <c>PostSell</c> with an entered fee (no schedule)
/// and an <see cref="AppliedFxRate"/> stored as the trade's FX detail.
/// </summary>
public class EnteredTradeNumbersTests
{
    private const int TenantId = 1;
    private const int AccountId = 2;
    private const int InstrumentId = 11;
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 17, 22, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly BookedDate = new(2026, 11, 17);
    private static readonly Currency Nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

    private static AppliedFxRate CallerRate(string baseCurrency = "USD", string quoteCurrency = "NZD", decimal rate = 1.69147m) =>
        AppliedFxRate.Create(baseCurrency, quoteCurrency, rate, BookedDate, AppliedFxRate.CallerSource);

    private static Transaction Buy(
        decimal quantity,
        decimal price,
        decimal? enteredFee,
        string priceCurrency = "NZD",
        string accountCurrency = "NZD",
        decimal fxRate = 1m,
        AppliedFxRate? appliedRate = null,
        TradeFeeSchedule? schedule = null,
        decimal currentCashSum = 100000m) =>
        Transaction.PostBuy(
            TenantId, AccountId, AccountType.Brokerage, accountCurrency, InstrumentId, quantity,
            Money.Create(price, priceCurrency), fxRate, BookedAt, null, null, currentCashSum,
            schedule, 2, enteredFee, appliedRate);

    private static Transaction Sell(
        decimal quantity,
        decimal price,
        decimal? enteredFee,
        string priceCurrency = "NZD",
        string accountCurrency = "NZD",
        decimal fxRate = 1m,
        AppliedFxRate? appliedRate = null,
        decimal currentCashSum = 0m,
        decimal currentQuantity = 100m,
        decimal currentCost = 249.5m) =>
        Transaction.PostSell(
            TenantId, AccountId, AccountType.Brokerage, accountCurrency, InstrumentId, quantity,
            Money.Create(price, priceCurrency), fxRate, BookedAt, null, null, currentCashSum,
            currentQuantity, currentCost, null, 2, enteredFee, appliedRate);

    private static TransactionCashLeg Leg(Transaction transaction, CashLegRole role) =>
        transaction.CashLegs.Single(leg => leg.Role == role);

    [Test]
    public void Buy_WithAnEnteredFee_BooksAFeeLegAndAFeeDetailWithoutSchedule()
    {
        var buy = Buy(100m, 2.345m, enteredFee: 15.00m);

        Leg(buy, CashLegRole.Principal).Amount.ShouldBe(-234.5000m);
        var fee = Leg(buy, CashLegRole.Fee);
        fee.Amount.ShouldBe(-15.0000m);
        fee.Currency.ShouldBe("NZD");
        var detail = fee.FeeDetail.ShouldNotBeNull();
        detail.Kind.ShouldBe(FeeKind.Brokerage);
        detail.TradeFeeScheduleId.ShouldBeNull();
        detail.BasisAmount.ShouldBe(234.5000m);
        detail.FeeAmount.ShouldBe(15.00m);
        detail.Currency.ShouldBe("NZD");
        buy.FeeDetails.ShouldBe([detail]);
        buy.SecurityLegs.Single().CostAmount.ShouldBe(249.5000m);
        buy.FxDetail.ShouldBeNull();
    }

    [Test]
    public void Sell_WithoutAFee_BooksOnePrincipalLegAndReleasesTheAverageCost()
    {
        var sell = Sell(40m, 2.50m, enteredFee: null);

        sell.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(100.0000m);
        sell.FeeDetails.ShouldBeEmpty();
        sell.SecurityLegs.Single().CostAmount.ShouldBe(-99.8000m);
    }

    [Test]
    public void Sell_WithAnEnteredFee_OnlyReducesCash_AndTheCashCheckCountsIt()
    {
        var sell = Sell(40m, 2.50m, enteredFee: 5.00m);

        sell.CashLegs.Select(leg => (leg.Role, leg.Amount))
            .ShouldBe([(CashLegRole.Principal, 100.0000m), (CashLegRole.Fee, -5.0000m)], ignoreOrder: true);
        Leg(sell, CashLegRole.Fee).FeeDetail!.TradeFeeScheduleId.ShouldBeNull();
        sell.SecurityLegs.Single().CostAmount.ShouldBe(-99.8000m);
        Should.Throw<DomainException>(() => Sell(1m, 1.00m, enteredFee: 5.00m, currentCashSum: 3.99m));
    }

    [Test]
    public void Buy_InAnotherCurrency_ConvertsTheFeeAndAttachesTheFxDetail()
    {
        var buy = Buy(10m, 180.00m, enteredFee: 9.95m, priceCurrency: "USD", accountCurrency: "NZD",
            fxRate: 1.69147m, appliedRate: CallerRate());

        Leg(buy, CashLegRole.Principal).Amount.ShouldBe(-3044.6460m);
        var fee = Leg(buy, CashLegRole.Fee);
        fee.Amount.ShouldBe(-16.8301m);
        fee.Currency.ShouldBe("NZD");
        fee.FeeDetail!.FeeAmount.ShouldBe(9.95m);
        fee.FeeDetail.Currency.ShouldBe("USD");
        fee.FeeDetail.BasisAmount.ShouldBe(1800.0000m);
        buy.SecurityLegs.Single().CostAmount.ShouldBe(1809.9500m);
        buy.SecurityLegs.Single().CostCurrency.ShouldBe("USD");
        var fx = buy.FxDetail.ShouldNotBeNull();
        (fx.BaseCurrency, fx.QuoteCurrency, fx.Rate, fx.RateAsOfDate, fx.RateSource)
            .ShouldBe(("USD", "NZD", 1.69147m, BookedDate, "Caller"));
    }

    [Test]
    public void Sell_InAnotherCurrency_AttachesTheFxDetail()
    {
        var sell = Sell(4m, 180.00m, enteredFee: null, priceCurrency: "USD", accountCurrency: "NZD",
            fxRate: 1.69147m, appliedRate: CallerRate(), currentQuantity: 10m, currentCost: 1809.95m);

        sell.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(1217.8584m);
        sell.FxDetail.ShouldNotBeNull().RateSource.ShouldBe("Caller");
    }

    [TestCase(0)]
    [TestCase(0.00004)]
    public void EnteredFee_ThatIsOrConvertsToZero_BooksNoFeeLeg(decimal enteredFee)
    {
        var buy = Buy(100m, 2.345m, enteredFee);

        buy.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        buy.FeeDetails.ShouldBeEmpty();
        buy.SecurityLegs.Single().CostAmount.ShouldBe(234.5000m);
    }

    [Test]
    public void EnteredFee_BelowZero_Throws()
    {
        Should.Throw<DomainException>(() => Buy(100m, 2.345m, enteredFee: -1m));
        Should.Throw<DomainException>(() => Sell(40m, 2.50m, enteredFee: -1m));
    }

    [Test]
    public void AScheduleAndAnEnteredFee_Throws()
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, Nzd, new DateOnly(2026, 11, 1), 10m, null, [new FeeBand(0m, 0.5m)], new DateOnly(2026, 10, 31));

        Should.Throw<DomainException>(() => Buy(100m, 2.345m, enteredFee: 15m, schedule: schedule));
    }

    [Test]
    public void AppliedRate_ThatDoesNotMatchTheTrade_Throws()
    {
        // Base is not the price currency.
        Should.Throw<DomainException>(() => Buy(10m, 180m, null, "USD", "NZD", 1.69147m, CallerRate("AUD", "NZD")));
        // Quote is not the account currency.
        Should.Throw<DomainException>(() => Buy(10m, 180m, null, "USD", "NZD", 1.69147m, CallerRate("USD", "AUD")));
        // The rate is not the one the cash legs use.
        Should.Throw<DomainException>(() => Buy(10m, 180m, null, "USD", "NZD", 1.69147m, CallerRate(rate: 1.7m)));
        Should.Throw<DomainException>(() =>
            Sell(1m, 180m, null, "USD", "NZD", 1.69147m, CallerRate(rate: 1.7m), currentQuantity: 10m, currentCost: 1800m));
    }

    [Test]
    public void Reverse_CopiesTheFeeDetailWithoutScheduleAndTheCallerFxDetail()
    {
        var buy = Buy(10m, 180.00m, enteredFee: 9.95m, priceCurrency: "USD", accountCurrency: "NZD",
            fxRate: 1.69147m, appliedRate: CallerRate());
        buy.Id = 40;

        var reversal = buy.Reverse(BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 0m });

        Leg(reversal, CashLegRole.Principal).Amount.ShouldBe(3044.6460m);
        var fee = Leg(reversal, CashLegRole.Fee);
        fee.Amount.ShouldBe(16.8301m);
        fee.FeeDetail.ShouldNotBeNull().TradeFeeScheduleId.ShouldBeNull();
        fee.FeeDetail.FeeAmount.ShouldBe(9.95m);
        var fx = reversal.FxDetail.ShouldNotBeNull();
        (fx.BaseCurrency, fx.QuoteCurrency, fx.Rate, fx.RateAsOfDate, fx.RateSource)
            .ShouldBe(("USD", "NZD", 1.69147m, BookedDate, "Caller"));
    }
}
