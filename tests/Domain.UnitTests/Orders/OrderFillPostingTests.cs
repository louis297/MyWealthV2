using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Orders;

/// <summary>Orders R32, R33, R35, R37 (docs/features/orders.md §4.6): the Transaction of an order fill.</summary>
public class OrderFillPostingTests
{
    private const int TenantId = 7;
    private const int AccountId = 3;
    private const int InstrumentId = 11;
    private const int ScheduleId = 9;
    private static readonly Guid OrderId = Guid.Parse("2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11");
    private static readonly Guid FillId = Guid.Parse("7d1a2f44-0f0e-4c59-8a41-3f5a8a1b2c3d");
    private static readonly DateTimeOffset BookedAt = new(2026, 11, 23, 2, 11, 2, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 23);
    private static readonly DateOnly Settles = new(2026, 11, 25);
    private static readonly Currency Usd = Currency.Create("USD", "US Dollar", 2);
    private static readonly TradeExecution Execution = new(FillId, Day, Settles, "CN-1");

    /// <summary>0.5 % with a minimum of 5.00.</summary>
    private static TradeFeeSchedule Schedule()
    {
        var schedule = TradeFeeSchedule.Create(
            TenantId, Usd, new DateOnly(2026, 11, 1), 5m, null, [new FeeBand(0m, 0.5m)], new DateOnly(2026, 10, 15));
        schedule.Id = ScheduleId;
        return schedule;
    }

    private static Transaction Buy(
        TransactionSource? source = null,
        TradeExecution? execution = null,
        bool noExecution = false,
        TradeFeeSchedule? schedule = null,
        decimal? fee = null,
        decimal quantity = 4m,
        decimal price = 100.12m) =>
        Transaction.PostBuy(
            TenantId, AccountId, AccountType.Brokerage, "USD", InstrumentId, quantity, Money.Create(price, "USD"), 1m, BookedAt,
            Transaction.BuyOrderMemo, null, source ?? TransactionSource.OrderFill(OrderId), currentCashSum: 5000m,
            feeSchedule: schedule, enteredFee: fee, execution: noExecution ? null : execution ?? Execution);

    private static Transaction Sell(
        TransactionSource? source = null,
        bool noExecution = false,
        TradeFeeSchedule? schedule = null,
        decimal? fee = null,
        string accountCurrency = "USD",
        decimal fxRate = 1m,
        AppliedFxRate? appliedRate = null) =>
        Transaction.PostSell(
            TenantId, AccountId, AccountType.Brokerage, accountCurrency, InstrumentId, 4m, Money.Create(100m, "USD"), fxRate,
            BookedAt, Transaction.SellOrderMemo, null, source ?? TransactionSource.OrderFill(OrderId), currentCashSum: 0m,
            currentQuantity: 10m, currentCost: 900m, feeSchedule: schedule, enteredFee: fee, appliedRate: appliedRate,
            execution: noExecution ? null : Execution);

    [Test]
    public void OrderFill_IsSourceKind8_AndKeepsTheOrdersId()
    {
        var source = TransactionSource.OrderFill(OrderId);

        source.Kind.ShouldBe(TransactionSourceKind.OrderFill);
        source.PublicId.ShouldBe(OrderId);
        ((int)TransactionSourceKind.OrderFill).ShouldBe(8);
    }

    [Test]
    public void OrderFill_WithAnEmptyId_Throws() =>
        Should.Throw<DomainException>(() => TransactionSource.OrderFill(Guid.Empty));

    [Test]
    public void AnOrderFill_IsNotHandReversibleOnACustodialAccount()
    {
        CustodyRules.AllowsHandReverse(CustodyMode.Custodial, TransactionSourceKind.OrderFill).ShouldBeFalse();
    }

    [Test]
    public void ABuyFill_HasTheOrderAsItsSource_AndItsExecutionDetail()
    {
        var buy = Buy();

        buy.Type.ShouldBe(TransactionType.Buy);
        (buy.SourceKind, buy.SourcePublicId).ShouldBe((TransactionSourceKind.OrderFill, (Guid?)OrderId));
        buy.Source.ShouldBe(TransactionSource.OrderFill(OrderId));
        buy.Memo.ShouldBe("Buy order");
        buy.Reference.ShouldBeNull();
        buy.BookedAt.ShouldBe(BookedAt);
        var detail = buy.ExecutionDetail.ShouldNotBeNull();
        (detail.TenantId, detail.FillPublicId, detail.Price, detail.TradeDate, detail.SettlementDate, detail.BrokerReference)
            .ShouldBe((TenantId, FillId, 100.12m, Day, Settles, "CN-1"));
        buy.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-400.48m);
        var security = buy.SecurityLegs.ShouldHaveSingleItem();
        (security.Quantity, security.CostAmount, security.CostCurrency).ShouldBe((4m, 400.48m, "USD"));
    }

    [Test]
    public void ABuyFill_IsChargedTheFeeItIsGiven_UnderTheOrdersSchedule_WithItsOwnGrossAsBasis()
    {
        // The second fill of the example of spec §8.1: the order's fee on 1001.50 is 5.01, of which 5.00 is charged.
        var buy = Buy(schedule: Schedule(), fee: 0.01m, quantity: 6m, price: 100.17m);

        buy.CashLegs.Count.ShouldBe(2);
        var fee = buy.CashLegs.Single(leg => leg.Role == CashLegRole.Fee);
        fee.Amount.ShouldBe(-0.01m);
        var detail = buy.FeeDetails.ShouldHaveSingleItem();
        (detail.Kind, detail.TradeFeeScheduleId, detail.BasisAmount, detail.FeeAmount, detail.Currency)
            .ShouldBe((FeeKind.Brokerage, (int?)ScheduleId, 601.02m, 0.01m, "USD"));
        buy.SecurityLegs.Single().CostAmount.ShouldBe(601.03m);
    }

    [Test]
    public void ABuyFill_WithAFeeOfZero_HasNoFeeLeg()
    {
        var buy = Buy(schedule: Schedule(), fee: 0m);

        buy.CashLegs.ShouldHaveSingleItem().Role.ShouldBe(CashLegRole.Principal);
        buy.FeeDetails.ShouldBeEmpty();
    }

    [Test]
    public void AFill_UnderASchedule_NeedsItsFee_AndWithoutOne_TakesNone()
    {
        Should.Throw<DomainException>(() => Buy(schedule: Schedule()));
        Should.Throw<DomainException>(() => Buy(fee: 5m));
        Should.Throw<DomainException>(() => Sell(schedule: Schedule()));
        Should.Throw<DomainException>(() => Sell(fee: 5m));
    }

    [Test]
    public void AFill_NeedsItsExecutionDetail_AndNoOtherSourceTakesOne()
    {
        Should.Throw<DomainException>(() => Buy(noExecution: true));
        Should.Throw<DomainException>(() => Sell(noExecution: true));
        Should.Throw<DomainException>(() => Buy(source: TransactionSource.Manual));
        Should.Throw<DomainException>(() => Buy(source: TransactionSource.Rebalance(OrderId)));
        Should.Throw<DomainException>(() => Sell(source: TransactionSource.Manual));
    }

    [Test]
    public void AFill_RefusesAnExecutionWithNoFill_OrThatSettlesBeforeItsTradeDate()
    {
        Should.Throw<DomainException>(() => Buy(execution: new TradeExecution(Guid.Empty, Day, Settles, null)));
        Should.Throw<DomainException>(() => Buy(execution: new TradeExecution(FillId, Day, Day.AddDays(-1), null)));
    }

    [Test]
    public void AFill_CutsTheBrokersReferenceTo50_AndKeepsNoneForABlankOne()
    {
        Buy(execution: Execution with { BrokerReference = new string('c', 60) }).ExecutionDetail!.BrokerReference
            .ShouldBe(new string('c', 50));
        Buy(execution: Execution with { BrokerReference = " " }).ExecutionDetail!.BrokerReference.ShouldBeNull();
    }

    [Test]
    public void ASellFill_ReleasesCostAtAverage_AndHasItsExecutionDetail_AndItsFee()
    {
        var sell = Sell(schedule: Schedule(), fee: 5m);

        sell.Type.ShouldBe(TransactionType.Sell);
        sell.Memo.ShouldBe("Sell order");
        (sell.SourceKind, sell.SourcePublicId).ShouldBe((TransactionSourceKind.OrderFill, (Guid?)OrderId));
        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(400m);
        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Fee).Amount.ShouldBe(-5m);
        var security = sell.SecurityLegs.ShouldHaveSingleItem();
        (security.Quantity, security.CostAmount).ShouldBe((-4m, -360m));
        var detail = sell.ExecutionDetail.ShouldNotBeNull();
        (detail.FillPublicId, detail.Price, detail.TradeDate, detail.SettlementDate).ShouldBe((FillId, 100m, Day, Settles));
        sell.FeeDetails.ShouldHaveSingleItem().BasisAmount.ShouldBe(400m);
    }

    [Test]
    public void ASellFill_InAnotherCurrency_ConvertsThePrincipalAndTheFee_AndKeepsTheRate()
    {
        var rate = AppliedFxRate.Create("USD", "NZD", 1.6m, Day, "Frankfurter");

        var sell = Sell(schedule: Schedule(), fee: 5m, accountCurrency: "NZD", fxRate: 1.6m, appliedRate: rate);

        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(640m);
        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Fee).Amount.ShouldBe(-8m);
        sell.CashLegs.ShouldAllBe(leg => leg.Currency == "NZD");
        sell.FxDetail.ShouldNotBeNull().Rate.ShouldBe(1.6m);
        sell.ExecutionDetail.ShouldNotBeNull();
    }

    [Test]
    public void Reverse_CopiesTheExecutionDetail()
    {
        var buy = Buy(schedule: Schedule(), fee: 5m);
        buy.Id = 42;

        var reversal = buy.Reverse(
            TransactionSource.OrderFill(OrderId), BookedAt.AddDays(1),
            new Dictionary<int, decimal> { [AccountId] = 0m },
            new Dictionary<(int AccountId, int InstrumentId), decimal> { [(AccountId, InstrumentId)] = 4m });

        var detail = reversal.ExecutionDetail.ShouldNotBeNull();
        detail.ShouldNotBeSameAs(buy.ExecutionDetail);
        (detail.TenantId, detail.FillPublicId, detail.Price, detail.TradeDate, detail.SettlementDate, detail.BrokerReference)
            .ShouldBe((TenantId, FillId, 100.12m, Day, Settles, "CN-1"));
    }

    [Test]
    public void ATradeThatIsNotAFill_HasNoExecutionDetail() =>
        Transaction.PostBuy(
                TenantId, AccountId, AccountType.Brokerage, "USD", InstrumentId, 4m, Money.Create(100m, "USD"), 1m, BookedAt,
                null, null, TransactionSource.Manual, currentCashSum: 5000m)
            .ExecutionDetail.ShouldBeNull();
}
