using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R26–R29, R32: the broker breaks a fill; the flow reverses its Transaction, and the
/// order takes the quantity back.</summary>
[Parallelizable]
public class BrokenTradeTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    [Test]
    public async Task ABrokenPostedBuy_IsReversedByTheFlow_WithTheOrdersSource_AndTheCashHoldingAndFeeComeBack()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fillReport) = await FilledAsync(world, 4m);
        var posted = (await OrderHttp.OrderAsync(id)).Fills.Single();
        var broken = Feed.AddBroken(id, fillReport);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 0, 0));
        (await BrokerHttp.ReportAsync(broken.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var fill = (await OrderHttp.OrderAsync(id)).Fills.Single();
        (fill.Status, fill.BreakOutcome, fill.BreakReason).ShouldBe((
            OrderFillStatus.Broken, (FillBreakOutcome?)FillBreakOutcome.Reversed, "Broken by the exchange"));
        var reversal = await ReversalAsync(posted.TransactionId!.Value);
        fill.ReversalTransactionId.ShouldBe(reversal.Id);
        (reversal.Type, reversal.SourceKind, reversal.SourcePublicId, reversal.CreatedBy).ShouldBe((
            TransactionType.Reversal, TransactionSourceKind.OrderFill, (Guid?)id, "system"));
        reversal.ExecutionDetail.ShouldNotBeNull().FillPublicId.ShouldBe(fill.PublicId);
        (await world.CashAsync()).ShouldBe((5000.00m, 1055.25m, 3944.75m));
        (await HoldingAsync(world)).ShouldBe(0m);
    }

    [Test]
    public async Task AnOpenOrder_TakesTheQuantityBack_AndCanBeFilledAgainWithoutOverfill()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fillReport) = await FilledAsync(world, 4m);
        Feed.AddBroken(id, fillReport);
        await BrokerHttp.RunReportsAsync();

        var afterBreak = await OrderHttp.OrderAsync(id);
        (afterBreak.Status, afterBreak.FilledQuantity, afterBreak.OpenQuantity).ShouldBe((OrderStatus.Working, 0m, 10m));
        var again = Feed.AddFill(id, 10m, 100.12m);
        await BrokerHttp.RunReportsAsync();

        (await BrokerHttp.ReportAsync(again.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var refilled = await OrderHttp.OrderAsync(id);
        (refilled.Status, refilled.FilledQuantity).ShouldBe((OrderStatus.Filled, 10m));
    }

    [Test]
    public async Task AFilledOrder_BecomesCancelled_WithTradeBroken()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fillReport) = await FilledAsync(world, 10m);
        Feed.AddBroken(id, fillReport);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.CloseReason, order.FilledQuantity, order.ReservedCash).ShouldBe((
            OrderStatus.Cancelled, Order.TradeBrokenReason, 0m, 0m));
        (await world.CashAsync()).ShouldBe((5000.00m, 0m, 5000.00m));
    }

    [Test]
    public async Task ABrokenPendingFill_IsNotPosted_NothingIsWritten_AndItIsNeverTriedAgain()
    {
        var world = await OrderWorld.CreateAsync(funding: 1055.25m);
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, 10m, 110m);
        await BrokerHttp.RunReportsAsync();
        Feed.AddBroken(id, fill.Id!);

        await BrokerHttp.RunReportsAsync();

        var stored = (await OrderHttp.OrderAsync(id)).Fills.Single();
        (stored.Status, stored.BreakOutcome, stored.TransactionId, stored.HoldReason).ShouldBe((
            OrderFillStatus.Broken, (FillBreakOutcome?)FillBreakOutcome.NotPosted, (int?)null, (FillHoldReason?)null));
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(0, 0, 0));
        (await world.CashAsync()).ShouldBe((1055.25m, 0m, 1055.25m));
    }

    [Test]
    public async Task ABrokenReportTwice_IsRepeated_AndForAnUnknownFill_IsUnknownFill()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fillReport) = await FilledAsync(world, 4m);
        Feed.AddBroken(id, fillReport);
        var twice = Feed.AddBroken(id, fillReport);
        var unknown = Feed.AddBroken(id, "fake-X999");

        await BrokerHttp.RunReportsAsync();

        (await BrokerHttp.ReportAsync(twice.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.Repeated);
        (await BrokerHttp.ReportAsync(unknown.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.UnknownFill);
        (await TransactionsOfOrderAsync(id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task ABrokenReportForASettledFill_IsFillSettled_AndASettledReportForABrokenFill_IsFillBroken()
    {
        var world = await OrderWorld.CreateAsync();
        var (settledOrder, settledFill) = await FilledAsync(world, 2m);
        Feed.AddSettled(settledOrder, settledFill);
        await BrokerHttp.RunReportsAsync();
        var lateBreak = Feed.AddBroken(settledOrder, settledFill);
        var (brokenOrder, brokenFill) = await FilledAsync(world, 3m);
        Feed.AddBroken(brokenOrder, brokenFill);
        var lateSettle = Feed.AddSettled(brokenOrder, brokenFill);

        await BrokerHttp.RunReportsAsync();

        (await BrokerHttp.ReportAsync(lateBreak.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.FillSettled);
        (await BrokerHttp.ReportAsync(lateSettle.Id!)).SkipReason.ShouldBe(ExecutionReportSkipReason.FillBroken);
        (await OrderHttp.OrderAsync(settledOrder)).Fills.Single().Status.ShouldBe(OrderFillStatus.Posted);
        (await OrderHttp.OrderAsync(brokenOrder)).Fills.Single().IsSettled.ShouldBeFalse();
    }

    [Test]
    public async Task ABrokenItemWithoutTheFillsOutsideId_OrAClientOrderId_IsRefused()
    {
        Feed.Add(new BrokerFeedItem(Feed.NextId(), ExecutionReportKind.Broken, Guid.NewGuid().ToString(), null, null, null, null, null,
            null, null, null, "Broken"));
        (await BrokerHttp.PullAsync()).Failure.ShouldBe(BrokerFailure.InvalidItem);

        Feed.Reset();
        Feed.Add(new BrokerFeedItem(Feed.NextId(), ExecutionReportKind.Broken, null, null, null, null, null, null, null, "fake-X1",
            null, "Broken"));
        (await BrokerHttp.PullAsync()).Failure.ShouldBe(BrokerFailure.InvalidItem);
    }

    [Test]
    public async Task ABrokenFill_IsLeftOutOfItsBrokerSettlement()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (kept, keptFill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
        var (broken, brokenFill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 1m, 100m);
        Feed.AddBroken(broken, brokenFill);
        Feed.AddSettled(broken, brokenFill, settlementId: "w-S1");
        Feed.AddSettled(kept, keptFill, settlementId: "w-S1");
        Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -400.48m, 1);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single();
        (settlement.Status, settlement.FillCount).ShouldBe((TradeSettlementStatus.AwaitingPayment, 1));
    }

    /// <summary>Places, sends and fills a buy of 10 by <paramref name="quantity"/> at 100.12; returns the order and the
    /// outside id of its fill.</summary>
    internal static async Task<(Guid OrderId, string FillReport)> FilledAsync(OrderWorld world, decimal quantity)
    {
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, quantity, 100.12m);
        await BrokerHttp.RunReportsAsync();
        return (id, fill.Id!);
    }

    internal static async Task<Transaction> ReversalAsync(int originalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking().Include(row => row.ExecutionDetail)
            .SingleAsync(row => row.OriginalTransactionId == originalId);
    }

    private static async Task<List<Transaction>> TransactionsOfOrderAsync(Guid orderId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking().Where(row => row.SourcePublicId == orderId).ToListAsync();
    }

    private static async Task<decimal> HoldingAsync(OrderWorld world)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == world.InstrumentId);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == world.AccountId);
        return await db.Holdings.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.AccountId == account.Id && row.InstrumentId == instrument.Id)
            .Select(row => (decimal?)row.Quantity)
            .SingleOrDefaultAsync() ?? 0m;
    }
}
