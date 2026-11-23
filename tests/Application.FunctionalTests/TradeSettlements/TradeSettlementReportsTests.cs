using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R6–R11, R13: <c>Settled</c> reports with a settlement id, then the <c>Settlement</c>
/// report that closes the broker settlement into a trade settlement.</summary>
[Parallelizable]
public class TradeSettlementReportsTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    private static FakeBrokerConnections Connections =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<FakeBrokerConnections>();

    [Test]
    public async Task SettledReportsWithAnId_ThenTheSettlementReport_CreateOneTradeSettlement_WithThePlatformsNet()
    {
        var (world, clientMoney) = await WorldAsync();
        var (first, firstFill) = await FilledAsync(world, "Buy", 4m, 100.12m);
        var (second, secondFill) = await FilledAsync(world, "Buy", 6m, 100.17m);
        var settledDate = OrderHttp.Today.AddDays(2);
        Feed.AddSettled(first, firstFill, settledDate, "w-S1");
        Feed.AddSettled(second, secondFill, settledDate, "w-S1");
        var closing = Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -1001.50m, 2, settledDate: settledDate);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((3, 0, 0));
        var report = await BrokerHttp.ReportAsync(closing.Id!);
        (report.Kind, report.Status, report.SettlementId, report.SettlementAccount, report.Currency, report.NetAmount, report.FillCount)
            .ShouldBe(((ExecutionReportKind?)ExecutionReportKind.Settlement, ExecutionReportStatus.Processed, "w-S1",
                clientMoney.AccountNumber, "USD", (decimal?)-1001.50m, (int?)2));
        var settlement = (await SettlementsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        (settlement.ClientMoneyAccountId, settlement.Connection, settlement.BrokerSettlementId, settlement.Currency, settlement.SettledDate)
            .ShouldBe((clientMoney.Id, "simulator", "w-S1", "USD", settledDate));
        (settlement.FillCount, settlement.NetAmount, settlement.BrokerFillCount, settlement.BrokerNetAmount)
            .ShouldBe((2, -1001.50m, 2, -1001.50m));
        settlement.ExecutionReportId.ShouldBe(report.Id);
        settlement.Status.ShouldBe(TradeSettlementStatus.AwaitingPayment);
        foreach (var id in new[] { first, second })
        {
            var fill = (await OrderHttp.OrderAsync(id)).Fills.Single();
            (fill.BrokerSettlementId, fill.TradeSettlementId, fill.SettledDate)
                .ShouldBe(("w-S1", (int?)settlement.Id, (DateOnly?)settledDate));
        }
    }

    [Test]
    public async Task ASettledReportWithoutAnId_SettlesAsBefore_AndNamesNoSettlement()
    {
        var (world, _) = await WorldAsync();
        var (id, fill) = await FilledAsync(world, "Buy", 4m, 100.12m);
        Feed.AddSettled(id, fill);

        await BrokerHttp.RunReportsAsync();

        var stored = (await OrderHttp.OrderAsync(id)).Fills.Single();
        (stored.IsSettled, stored.BrokerSettlementId, stored.TradeSettlementId).ShouldBe((true, (string?)null, (int?)null));
    }

    [Test]
    public async Task APendingFill_CountsInThePlatformsNet()
    {
        Connections.SetBankAccount("USD");
        var world = await OrderWorld.CreateAsync(funding: 1055.25m);
        var clientMoney = await SettlementAccountTests.ClientMoneyAsync(world.Tenant.Id, "USD");
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, 10m, 110m);
        await BrokerHttp.RunReportsAsync();
        (await OrderHttp.OrderAsync(id)).Fills.Single().Status.ShouldBe(OrderFillStatus.Pending);
        Feed.AddSettled(id, fill.Id!, settlementId: "w-S1");
        Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -1100m, 1);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await SettlementsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        (settlement.FillCount, settlement.NetAmount, settlement.Status)
            .ShouldBe((1, -1100m, TradeSettlementStatus.AwaitingPayment));
    }

    [Test]
    public async Task ASaleThatNetsAbove0_AwaitsTheBrokersReceipt()
    {
        var (world, clientMoney) = await WorldAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (id, fill) = await FilledAsync(world, "Sell", 10m, 99m);
        Feed.AddSettled(id, fill, settlementId: "w-S2");
        Feed.AddSettlement("w-S2", clientMoney.AccountNumber, 990m, 1);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await SettlementsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        (settlement.NetAmount, settlement.Status).ShouldBe((990m, TradeSettlementStatus.AwaitingReceipt));
    }

    [Test]
    public async Task ABuyAndASellThatNetTo0_AreCompletedAtOnce()
    {
        var (world, clientMoney) = await WorldAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (buy, buyFill) = await FilledAsync(world, "Buy", 2m, 100m);
        var (sell, sellFill) = await FilledAsync(world, "Sell", 2m, 100m);
        Feed.AddSettled(buy, buyFill, settlementId: "w-S3");
        Feed.AddSettled(sell, sellFill, settlementId: "w-S3");
        Feed.AddSettlement("w-S3", clientMoney.AccountNumber, 0m, 2);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await SettlementsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        (settlement.NetAmount, settlement.Status).ShouldBe((0m, TradeSettlementStatus.Completed));
        settlement.CompletedAt.ShouldNotBeNull();
    }

    [TestCase(-1001.49, 1, null, "USD")]
    [TestCase(-400.48, 2, null, "USD")]
    [TestCase(-400.48, 1, "OTHER-ACCOUNT", "USD")]
    [TestCase(-400.48, 1, null, "NZD")]
    public async Task OtherFigures_AreDisputed_AndKeepTheBrokers(double net, int count, string? account, string currency)
    {
        var (world, clientMoney) = await WorldAsync();
        var (id, fill) = await FilledAsync(world, "Buy", 4m, 100.12m);
        Feed.AddSettled(id, fill, settlementId: "w-S4");
        Feed.AddSettlement("w-S4", account ?? clientMoney.AccountNumber, (decimal)net, count, currency);

        await BrokerHttp.RunReportsAsync();

        var settlement = (await SettlementsAsync(world.Tenant.Id)).ShouldHaveSingleItem();
        (settlement.Status, settlement.FillCount, settlement.NetAmount).ShouldBe((TradeSettlementStatus.Disputed, 1, -400.48m));
        (settlement.BrokerFillCount, settlement.BrokerNetAmount, settlement.BrokerSettlementAccount)
            .ShouldBe((count, (decimal)net, account ?? clientMoney.AccountNumber));
    }

    [Test]
    public async Task ASettlementReportWithNoFills_IsSkippedAsUnknownSettlement_AndASecondOneAsRepeated()
    {
        var (world, clientMoney) = await WorldAsync();
        var (id, fill) = await FilledAsync(world, "Buy", 4m, 100.12m);
        var unknown = Feed.AddSettlement("w-S9", clientMoney.AccountNumber, -400.48m, 1);
        Feed.AddSettled(id, fill, settlementId: "w-S5");
        Feed.AddSettlement("w-S5", clientMoney.AccountNumber, -400.48m, 1);
        var again = Feed.AddSettlement("w-S5", clientMoney.AccountNumber, -400.48m, 1);

        await BrokerHttp.RunReportsAsync();

        var skipped = await BrokerHttp.ReportAsync(unknown.Id!);
        (skipped.Status, skipped.SkipReason).ShouldBe((ExecutionReportStatus.Skipped, (ExecutionReportSkipReason?)ExecutionReportSkipReason.UnknownSettlement));
        var repeated = await BrokerHttp.ReportAsync(again.Id!);
        (repeated.Status, repeated.SkipReason).ShouldBe((ExecutionReportStatus.Skipped, (ExecutionReportSkipReason?)ExecutionReportSkipReason.Repeated));
        (await SettlementsAsync(world.Tenant.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task TheSettlementReportNeedsNoClientOrderId_ButAPageWithABadSettlementItemIsRefused()
    {
        var (_, clientMoney) = await WorldAsync();
        Feed.Add(new Application.Broker.BrokerFeedItem(
            Feed.NextId(), ExecutionReportKind.Settlement, null, null, null, null, null, null, null, null, OrderHttp.Today, null,
            new Domain.Orders.ExecutionReportSettlement("w-S6", clientMoney.AccountNumber, "USD", -1.00001m, 1)));

        var outcome = await BrokerHttp.PullAsync();

        outcome.Failure.ShouldBe(BrokerFailure.InvalidItem);
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
    }

    [TestCase(null, "SIM", "USD", 1)]
    [TestCase("w-S7", null, "USD", 1)]
    [TestCase("w-S7", "SIM", "US", 1)]
    [TestCase("w-S7", "SIM", "USD", 0)]
    [TestCase("w-S7", "SIM", "USD", null)]
    public async Task ASettlementItemWithoutItsFields_IsRefused(string? settlementId, string? account, string? currency, int? count)
    {
        Feed.Add(new Application.Broker.BrokerFeedItem(
            Feed.NextId(), ExecutionReportKind.Settlement, null, null, null, null, null, null, null, null, OrderHttp.Today, null,
            new Domain.Orders.ExecutionReportSettlement(settlementId, account, currency, -1m, count)));

        (await BrokerHttp.PullAsync()).Failure.ShouldBe(BrokerFailure.InvalidItem);
    }

    [Test]
    public async Task WithdrawableCash_RisesAtTheSettledReport_BeforeTheBankLine()
    {
        var (world, clientMoney) = await WorldAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var (id, fill) = await FilledAsync(world, "Sell", 10m, 99m);
        Feed.AddSettled(id, fill, settlementId: "w-S8");
        Feed.AddSettlement("w-S8", clientMoney.AccountNumber, 990m, 1);

        await BrokerHttp.RunReportsAsync();

        (await SettlementsAsync(world.Tenant.Id)).Single().Status.ShouldBe(TradeSettlementStatus.AwaitingReceipt);
        var cash = await world.CashAsync();
        cash.Cash.ShouldBe(cash.Available);
        (await UnsettledAsync(world)).ShouldBe(0m);
    }

    internal static async Task<(OrderWorld World, ClientMoneyAccount ClientMoney)> WorldAsync(
        string name = "North Advisory",
        string code = "north-advisory")
    {
        Connections.SetBankAccount("USD");
        var world = await OrderWorld.CreateAsync(name, code);
        return (world, await SettlementAccountTests.ClientMoneyAsync(world.Tenant.Id, "USD"));
    }

    /// <summary>Places, sends and fills one order in full; returns it and the outside id of its fill.</summary>
    internal static async Task<(Guid OrderId, string FillReport)> FilledAsync(OrderWorld world, string side, decimal quantity, decimal price)
    {
        var id = await world.PlaceAsync(side, quantity);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, quantity, price);
        await BrokerHttp.RunReportsAsync();
        return (id, fill.Id!);
    }

    internal static async Task<List<TradeSettlement>> SettlementsAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.TradeSettlements.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    private static async Task<decimal> UnsettledAsync(OrderWorld world)
    {
        var response = await Accounts.AccountHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}", world.TenantAdmin);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("unsettledCash").GetDecimal();
    }
}
