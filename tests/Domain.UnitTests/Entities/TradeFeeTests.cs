using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

/// <summary>Trade fees R14–R18, R20: the <c>Fee</c> cash leg on <c>Buy</c> / <c>Sell</c>.</summary>
public class TradeFeeTests
{
    private const int TenantId = 1;
    private const int AccountId = 2;
    private const int InstrumentId = 11;
    private const int ScheduleId = 7;
    private static readonly DateTimeOffset BookedAt = new(2026, 12, 2, 21, 0, 0, TimeSpan.Zero);
    private static readonly Currency Nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);

    /// <summary>0.5% to 10,000 then 0.3%, minimum 10, no cap.</summary>
    private static TradeFeeSchedule Schedule(Currency? currency = null, decimal minimum = 10m, FeeBand[]? bands = null)
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, currency ?? Nzd, new DateOnly(2026, 12, 1), minimum, null,
            bands ?? [new FeeBand(0m, 0.5m), new FeeBand(10000m, 0.3m)], new DateOnly(2026, 11, 15));
        schedule.Id = ScheduleId;
        return schedule;
    }

    private static Transaction Buy(
        decimal quantity,
        decimal price,
        TradeFeeSchedule? schedule,
        string priceCurrency = "NZD",
        string accountCurrency = "NZD",
        decimal fxRate = 1m,
        decimal currentCashSum = 100000m) =>
        Transaction.PostBuy(
            TenantId, AccountId, AccountType.Brokerage, accountCurrency, InstrumentId, quantity,
            Money.Create(price, priceCurrency), fxRate, BookedAt, null, null, source: TestSources.Manual, currentCashSum,
            schedule, 2);

    private static Transaction Sell(
        decimal quantity,
        decimal price,
        TradeFeeSchedule? schedule,
        string priceCurrency = "NZD",
        string accountCurrency = "NZD",
        decimal fxRate = 1m,
        decimal currentCashSum = 0m,
        decimal currentQuantity = 100m,
        decimal currentCost = 2000m) =>
        Transaction.PostSell(
            TenantId, AccountId, AccountType.Brokerage, accountCurrency, InstrumentId, quantity,
            Money.Create(price, priceCurrency), fxRate, BookedAt, null, null, source: TestSources.Manual, currentCashSum,
            currentQuantity, currentCost, schedule, 2);

    private static TransactionCashLeg Leg(Transaction transaction, CashLegRole role) =>
        transaction.CashLegs.Single(leg => leg.Role == role);

    [Test]
    public void Buy_WithSchedule_AddsAFeeLegAndCapitalisesTheFee()
    {
        var buy = Buy(50m, 25.00m, Schedule());

        buy.CashLegs.Count.ShouldBe(2);
        Leg(buy, CashLegRole.Principal).Amount.ShouldBe(-1250m);
        var fee = Leg(buy, CashLegRole.Fee);
        fee.Amount.ShouldBe(-10m);
        fee.AccountId.ShouldBe(AccountId);
        fee.Currency.ShouldBe("NZD");
        buy.SecurityLegs.Single().CostAmount.ShouldBe(1260m);
        buy.SecurityLegs.Single().CostCurrency.ShouldBe("NZD");
    }

    [Test]
    public void Buy_WithSchedule_WritesOneBrokerageFeeDetailOnTheFeeLeg()
    {
        var buy = Buy(50m, 25.00m, Schedule());

        var fee = Leg(buy, CashLegRole.Fee);
        var detail = fee.FeeDetail.ShouldNotBeNull();
        detail.Kind.ShouldBe(FeeKind.Brokerage);
        detail.TradeFeeScheduleId.ShouldBe(ScheduleId);
        detail.BasisAmount.ShouldBe(1250m);
        detail.FeeAmount.ShouldBe(10m);
        detail.Currency.ShouldBe("NZD");
        detail.TenantId.ShouldBe(TenantId);
        buy.FeeDetails.ShouldBe([detail]);
        Leg(buy, CashLegRole.Principal).FeeDetail.ShouldBeNull();
    }

    [Test]
    public void Buy_FeeDetail_MatchesTheScheduleCalculation()
    {
        var schedule = Schedule(minimum: 0m);
        var buy = Buy(600m, 25.00m, schedule);

        var detail = Leg(buy, CashLegRole.Fee).FeeDetail!;
        detail.FeeAmount.ShouldBe(schedule.FeeFor(15000m, 2).Fee);
        detail.FeeAmount.ShouldBe(65m);
    }

    [Test]
    public void Buy_CrossCurrency_ConvertsTheFeeWithThePrincipalRate()
    {
        var buy = Buy(50m, 25.00m, Schedule(Usd), priceCurrency: "USD", accountCurrency: "NZD", fxRate: 1.6543m);

        Leg(buy, CashLegRole.Principal).Amount.ShouldBe(-2067.875m);
        Leg(buy, CashLegRole.Fee).Amount.ShouldBe(-16.543m);
        Leg(buy, CashLegRole.Fee).Currency.ShouldBe("NZD");
        var detail = Leg(buy, CashLegRole.Fee).FeeDetail!;
        detail.FeeAmount.ShouldBe(10m);
        detail.Currency.ShouldBe("USD");
        buy.SecurityLegs.Single().CostAmount.ShouldBe(1260m);
        buy.SecurityLegs.Single().CostCurrency.ShouldBe("USD");
    }

    [Test]
    public void Buy_CashCheckIncludesTheFee()
    {
        Should.Throw<DomainException>(() => Buy(50m, 25.00m, Schedule(), currentCashSum: 1255m));
        Buy(50m, 25.00m, Schedule(), currentCashSum: 1260m).CashLegs.Sum(leg => leg.Amount).ShouldBe(-1260m);
    }

    [Test]
    public void Sell_WithSchedule_FeeOnlyReducesCash()
    {
        var sell = Sell(50m, 25.00m, Schedule());

        Leg(sell, CashLegRole.Principal).Amount.ShouldBe(1250m);
        Leg(sell, CashLegRole.Fee).Amount.ShouldBe(-10m);
        sell.SecurityLegs.Single().Quantity.ShouldBe(-50m);
        sell.SecurityLegs.Single().CostAmount.ShouldBe(-1000m);
        var detail = Leg(sell, CashLegRole.Fee).FeeDetail!;
        detail.BasisAmount.ShouldBe(1250m);
        detail.FeeAmount.ShouldBe(10m);
    }

    [Test]
    public void Sell_FeeAbovePrincipal_IsAllowedWhenCashCovers_AndRefusedOtherwise()
    {
        Should.Throw<DomainException>(() => Sell(1m, 1.00m, Schedule(), currentCashSum: 5m));

        var sell = Sell(1m, 1.00m, Schedule(), currentCashSum: 9m);
        Leg(sell, CashLegRole.Principal).Amount.ShouldBe(1m);
        Leg(sell, CashLegRole.Fee).Amount.ShouldBe(-10m);
    }

    [Test]
    public void Sell_CrossCurrency_ConvertsTheFeeWithThePrincipalRate()
    {
        var sell = Sell(50m, 25.00m, Schedule(Usd), priceCurrency: "USD", accountCurrency: "NZD", fxRate: 1.6543m);

        Leg(sell, CashLegRole.Principal).Amount.ShouldBe(2067.875m);
        Leg(sell, CashLegRole.Fee).Amount.ShouldBe(-16.543m);
    }

    [Test]
    public void FeeLeg_IsRoundedToFourDecimalsAfterConversion()
    {
        var buy = Buy(50m, 25.00m, Schedule(Usd), priceCurrency: "USD", accountCurrency: "NZD", fxRate: 1.23456789m);

        Leg(buy, CashLegRole.Fee).Amount.ShouldBe(-12.3457m);
    }

    [Test]
    public void NoSchedule_NoFeeLegAndNoDetail()
    {
        var buy = Buy(50m, 25.00m, null);
        var sell = Sell(50m, 25.00m, null);

        buy.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        sell.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        buy.FeeDetails.ShouldBeEmpty();
        buy.SecurityLegs.Single().CostAmount.ShouldBe(1250m);
    }

    [Test]
    public void ZeroFee_NoFeeLegAndNoDetail()
    {
        var zero = Schedule(minimum: 0m, bands: [new FeeBand(0m, 0m)]);

        var buy = Buy(50m, 25.00m, zero);

        buy.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        buy.FeeDetails.ShouldBeEmpty();
        buy.SecurityLegs.Single().CostAmount.ShouldBe(1250m);
    }

    [Test]
    public void ScheduleForAnotherCurrency_IsRefused()
    {
        Should.Throw<DomainException>(() => Buy(50m, 25.00m, Schedule(Usd)));
        Should.Throw<DomainException>(() => Sell(50m, 25.00m, Schedule(Usd)));
    }

    [Test]
    public void OtherTypes_HaveOnlyPrincipalLegs()
    {
        var transferIn = Transaction.Post(TenantId, AccountId, TransactionType.TransferIn, 100m, "NZD", BookedAt, null, null, source: TestSources.Manual, 0m, 1);
        var stockIn = Transaction.PostStockIn(
            TenantId, AccountId, AccountType.Brokerage, InstrumentId, 10m, 100m, "NZD", BookedAt, null, null, source: TestSources.Manual);

        transferIn.CashLegs.ShouldAllBe(leg => leg.Role == CashLegRole.Principal && leg.FeeDetail == null);
        stockIn.CashLegs.ShouldAllBe(leg => leg.Role == CashLegRole.Principal);
        transferIn.FeeDetails.ShouldBeEmpty();
        stockIn.FeeDetails.ShouldBeEmpty();
    }

    [Test]
    public void Reverse_OfABuyWithAFee_CopiesRolesAndTheFeeDetail_AndRefundsTheFee()
    {
        var buy = Buy(50m, 25.00m, Schedule());
        buy.Id = 40;

        var reversal = buy.Reverse(TestSources.Manual, BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 0m });

        Leg(reversal, CashLegRole.Principal).Amount.ShouldBe(1250m);
        Leg(reversal, CashLegRole.Principal).FeeDetail.ShouldBeNull();
        var fee = Leg(reversal, CashLegRole.Fee);
        fee.Amount.ShouldBe(10m);
        fee.AccountId.ShouldBe(AccountId);
        var detail = fee.FeeDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(Leg(buy, CashLegRole.Fee).FeeDetail);
        detail.Kind.ShouldBe(FeeKind.Brokerage);
        detail.TradeFeeScheduleId.ShouldBe(ScheduleId);
        detail.BasisAmount.ShouldBe(1250m);
        detail.FeeAmount.ShouldBe(10m);
        detail.Currency.ShouldBe("NZD");
        reversal.FeeDetails.ShouldBe([detail]);
        reversal.SecurityLegs.Single().CostAmount.ShouldBe(-1260m);
    }

    [Test]
    public void Reverse_OfASellWithAFee_CashCheckCountsTheRefund()
    {
        var sell = Sell(50m, 25.00m, Schedule());
        sell.Id = 41;

        Should.Throw<DomainException>(() => sell.Reverse(TestSources.Manual, BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 1239m }));
        var reversal = sell.Reverse(TestSources.Manual, BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 1240m });

        Leg(reversal, CashLegRole.Principal).Amount.ShouldBe(-1250m);
        Leg(reversal, CashLegRole.Fee).Amount.ShouldBe(10m);
        reversal.FeeDetails.ShouldHaveSingleItem().FeeAmount.ShouldBe(10m);
    }

    [Test]
    public void Reverse_WithoutAFee_HasOnlyPrincipalLegs()
    {
        var buy = Buy(50m, 25.00m, null);
        buy.Id = 42;

        var reversal = buy.Reverse(TestSources.Manual, BookedAt.AddDays(1), new Dictionary<int, decimal> { [AccountId] = 0m });

        reversal.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        reversal.FeeDetails.ShouldBeEmpty();
    }
}
