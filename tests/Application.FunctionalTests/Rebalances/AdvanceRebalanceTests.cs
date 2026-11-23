using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Rebalances;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Rebalances;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Rebalances;

/// <summary>Rebalances R14–R18, R21, R26 (docs/features/rebalances.md §10, waves): the advance step of the broker
/// job places the buys when the sells are in the ledger, one top-up, and ends the rebalance.</summary>
public class AdvanceRebalanceTests : TestBase
{
    [Test]
    public async Task SellsThenBuysThenATopUp_Completes_WithEveryTradeAnOrderFill()
    {
        var world = await RebalanceWorld.CreateAsync();
        var old = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD", "USD", 25m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, old, 40m, price: 25m);
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);

        // Wave 1: the sell. The advance step places the buys from the cash that is now there.
        var sells = await RebalanceHttp.FillWaveAsync(id);
        sells.ShouldHaveSingleItem().Side.ShouldBe(OrderSide.Sell);
        var afterSells = await RebalanceHttp.RebalanceAsync(id);
        afterSells.Status.ShouldBe(RebalanceStatus.Buying);
        (afterSells.Waves, afterSells.BuyWaves).ShouldBe((2, 1));
        var buys = (await RebalanceHttp.OrdersOfAsync(id)).Where(order => order.Side == OrderSide.Buy).ToList();
        buys.Count.ShouldBe(2);
        var (cash, reserved, available) = await world.Orders.CashAsync();
        reserved.ShouldBe(buys.Sum(order => order.ReservedCash));
        available.ShouldBeGreaterThanOrEqualTo(0m);
        // The sale has not settled, and its proceeds are used by the buys.
        reserved.ShouldBeGreaterThan(9000m);
        cash.ShouldBeGreaterThan(9900m);

        // Wave 2: the buys; then the top-up.
        await RebalanceHttp.FillWaveAsync(id);
        var afterBuys = await RebalanceHttp.RebalanceAsync(id);
        afterBuys.Status.ShouldBe(RebalanceStatus.Buying);
        (afterBuys.Waves, afterBuys.BuyWaves).ShouldBe((3, 2));

        // Wave 3: the top-up; no third buy wave.
        await RebalanceHttp.FillWaveAsync(id);
        var ended = await RebalanceHttp.RebalanceAsync(id);
        ended.Status.ShouldBe(RebalanceStatus.Completed);
        ended.EndedAt.ShouldNotBeNull();
        var orders = await RebalanceHttp.OrdersOfAsync(id);
        orders.ShouldAllBe(order => order.Status == OrderStatus.Filled);
        orders.Count.ShouldBeInRange(4, 5);
        (await BrokerHttp.AdvanceAsync()).ShouldBe(new RebalanceAdvanceOutcome(0, 0));

        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("orders").EnumerateArray().Select(order => order.GetProperty("wave").GetInt32()).Distinct()
            .ShouldBe([1, 2, 3]);

        // R26: every trade is an order fill; no Transaction is written with source Rebalance.
        var sources = await SourcesAsync(world.AccountId);
        sources.ShouldNotContain(TransactionSourceKind.Rebalance);
        sources.Count(kind => kind == TransactionSourceKind.OrderFill).ShouldBe(orders.Count);
        (await world.Orders.CashAsync()).Available.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Test]
    public async Task APlanWithNoSell_StartsBuying_AndCompletesAfterTheTopUp()
    {
        var world = await RebalanceWorld.CreateAsync();

        var id = await CustodialHttp.RebalanceAsync(world.Adviser, world.AccountId);

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        rebalance.Status.ShouldBe(RebalanceStatus.Completed);
        rebalance.BuyWaves.ShouldBe(RebalanceRules.MaxBuyWaves);
        var orders = await RebalanceHttp.OrdersOfAsync(id);
        orders.ShouldAllBe(order => order.Side == OrderSide.Buy && order.Status == OrderStatus.Filled);
        orders.Where(order => order.InstrumentId == orders[0].InstrumentId).Sum(order => order.Quantity).ShouldBeInRange(58m, 59.5m);
    }

    [Test]
    public async Task WhileAnOrderIsLive_TheAdvanceStepWaits()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        var first = (await RebalanceHttp.OrdersOfAsync(id))[0];
        BrokerHttp.Feed.AddFill(first.PublicId, 1m, first.ReferencePrice, "CN-P1");
        await BrokerHttp.RunReportsAsync();

        (await BrokerHttp.AdvanceAsync()).Advanced.ShouldBe(0);

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        (rebalance.Status, rebalance.Waves).ShouldBe((RebalanceStatus.Buying, 1));
    }

    [TestCase(ExecutionReportKind.Expired)]
    [TestCase(ExecutionReportKind.Cancelled)]
    public async Task ASellThatEndsUnfilled_EndsTheRebalanceIncomplete_AndNoBuyIsPlaced(ExecutionReportKind kind)
    {
        var world = await RebalanceWorld.CreateAsync();
        var old = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD", "USD", 25m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, old, 40m, price: 25m);
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        var sell = (await RebalanceHttp.OrdersOfAsync(id)).Single();
        BrokerHttp.Feed.AddEnd(sell.PublicId, kind, "End of day");
        await BrokerHttp.RunReportsAsync();

        await BrokerHttp.AdvanceAsync();

        await ShouldBeNotFilledAsync(world, id, "1 of 1 orders were not filled in full.");
        (await RebalanceHttp.OrdersOfAsync(id)).ShouldAllBe(order => order.Side == OrderSide.Sell);
    }

    [Test]
    public async Task AnOrderTheBrokerRejects_EndsTheRebalanceIncomplete()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        BrokerHttp.Orders.Answer(BrokerPlaceResult.Rejected(OrderRejectReason.Refused));
        var accepted = (await RebalanceHttp.OrdersOfAsync(id))[1];

        await BrokerHttp.SendAsync();
        BrokerHttp.Feed.AddFill(accepted.PublicId, accepted.Quantity, accepted.ReferencePrice, "CN-R1");
        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.AdvanceAsync();

        await ShouldBeNotFilledAsync(world, id, "1 of 2 orders were not filled in full.");
        (await RebalanceHttp.OrdersOfAsync(id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task AFillBrokenWhileTheRebalanceIsInProgress_EndsItIncomplete()
    {
        var world = await RebalanceWorld.CreateAsync();
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BrokerHttp.SendAsync();
        var orders = await RebalanceHttp.OrdersOfAsync(id);
        var fill = BrokerHttp.Feed.AddFill(orders[0].PublicId, orders[0].Quantity, orders[0].ReferencePrice, "CN-B1");
        BrokerHttp.Feed.AddFill(orders[1].PublicId, orders[1].Quantity, orders[1].ReferencePrice, "CN-B2");
        await BrokerHttp.RunReportsAsync();
        BrokerHttp.Feed.AddBroken(orders[0].PublicId, fill.Id!);
        await BrokerHttp.RunReportsAsync();

        await BrokerHttp.AdvanceAsync();

        await ShouldBeNotFilledAsync(world, id, "1 of 2 orders were not filled in full.");
    }

    [Test]
    public async Task APriceThatWentStaleBeforeTheBuyWave_EndsTheRebalanceIncompleteWithStalePrice()
    {
        var world = await RebalanceWorld.CreateAsync();
        var old = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "OLD", "USD", 25m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, old, 40m, price: 25m);
        var (_, id) = await world.ProposeAsync();
        await RebalanceHttp.ConfirmedAsync(world.Adviser, id);
        await BackdatePricesAsync(world.BetaId, days: 5);

        await RebalanceHttp.FillWaveAsync(id);

        var rebalance = await RebalanceHttp.RebalanceAsync(id);
        rebalance.Status.ShouldBe(RebalanceStatus.Incomplete);
        rebalance.FailureCode.ShouldBe("stale_price");
        (await RebalanceHttp.OrdersOfAsync(id)).ShouldAllBe(order => order.Side == OrderSide.Sell);
    }

    private static async Task ShouldBeNotFilledAsync(RebalanceWorld world, Guid id, string message)
    {
        var item = await RebalanceHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("Incomplete");
        item.GetProperty("failure").GetProperty("code").GetString().ShouldBe("order_not_filled");
        item.GetProperty("failure").GetProperty("message").GetString().ShouldBe(message);
        item.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    private static async Task<List<TransactionSourceKind>> SourcesAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var ids = await db.TransactionCashLegs.IgnoreQueryFilters()
            .Where(leg => leg.AccountId == account.Id)
            .Select(leg => leg.TransactionId)
            .Distinct()
            .ToListAsync();
        return (await db.Transactions.IgnoreQueryFilters().Where(row => ids.Contains(row.Id) && row.Type != TransactionType.TransferIn).ToListAsync())
            .Where(row => row.SourceKind != TransactionSourceKind.Manual)
            .Select(row => row.SourceKind)
            .ToList();
    }

    /// <summary>Moves every stored price of the instrument back by <paramref name="days"/>.</summary>
    private static async Task BackdatePricesAsync(Guid instrumentPublicId, int days)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentPublicId);
        var prices = await db.InstrumentPrices.Where(row => row.InstrumentId == instrument.Id).ToListAsync();
        foreach (var price in prices)
        {
            db.InstrumentPrices.Remove(price);
        }

        await db.SaveChangesAsync();
        foreach (var price in prices)
        {
            db.InstrumentPrices.Add(new() { InstrumentId = price.InstrumentId, AsOfDate = price.AsOfDate.AddDays(-days), Price = price.Price, Source = price.Source, FetchedAt = price.FetchedAt });
        }

        await db.SaveChangesAsync();
    }
}
