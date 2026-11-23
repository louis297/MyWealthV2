using System.Net;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R22–R25: the bank reconciliation explains the cash of order fills by their trade
/// settlement.</summary>
[Parallelizable]
public class TradeSettlementReconciliationTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    [Test]
    public async Task APostedBuyWaitingForItsSettlement_IsUnsettledTrades_AndItsFeeIsTradeFees_AndTheRunAgrees()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("UnsettledTrades", "Timing", 400.48m, 1), ("TradeFees", "Known", 5.00m, 1)]);
    }

    [Test]
    public async Task ABuyAndASell_AddUp_InUnsettledTradesAndTradeFees()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
        await TradeSettlementReportsTests.FilledAsync(world, "Sell", 10m, 99m);

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        // The sell's 990.00 is cash the broker owes; the buy's 400.48 cash the firm owes. The holding typed in before
        // orders stays in Trades.
        Items(run).ShouldBe([
            ("UnsettledTrades", "Timing", -589.52m, 2), ("Trades", "Known", 1000.00m, 1), ("TradeFees", "Known", 10.00m, 2)
        ]);
    }

    [Test]
    public async Task AfterTheSettlementCompletes_OnlyTheTradeFeesAreLeft_AndThereAreNoOpenSettlements()
    {
        var (world, clientMoney, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        var before = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);
        await BankFeedHttp.SendAsync();
        var payment = (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Single();
        BankFeedHttp.Feed.Add(clientMoney.AccountNumber, TradeSettlementPaymentTests.Debit(400.48m, payment.InstructionPublicId));
        await BankFeedHttp.RunAsync();

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        var open = before.GetProperty("openSettlements");
        (open.GetProperty("count").GetInt32(), open.GetProperty("amount").GetDecimal()).ShouldBe((1, -400.48m));
        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("TradeFees", "Known", 5.00m, 1)]);
        run.GetProperty("openSettlements").GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task APaymentFailedSettlement_IsTradesNeedingAction_AndTheRunHasBreaks()
    {
        var (world, clientMoney, _) = await TradeSettlementPaymentTests.PayingAsync();
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.Refused));
        await BankFeedHttp.SendAsync();

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Breaks");
        Items(run).ShouldBe([("TradesNeedingAction", "Break", 400.48m, 1), ("TradeFees", "Known", 5.00m, 1)]);
    }

    [Test]
    public async Task ADisputedSettlement_IsTradesNeedingAction()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (id, fill) = await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);
        Feed.AddSettled(id, fill, settlementId: "w-S4");
        Feed.AddSettlement("w-S4", clientMoney.AccountNumber, -1m, 1);
        await BrokerHttp.RunReportsAsync();

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Breaks");
        Items(run).ShouldBe([("TradesNeedingAction", "Break", 400.48m, 1), ("TradeFees", "Known", 5.00m, 1)]);
    }

    [Test]
    public async Task ASetAsideSettlement_PutsItsFillsBackInTrades()
    {
        var (world, clientMoney, settlement) = await TradeSettlementPaymentTests.PayingAsync();
        (await TradeSettlementReadAndSetAsideTests.SetAsideAsync(world.TenantAdmin, settlement, "Paid by hand")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("Trades", "Known", 405.48m, 1)]);
    }

    [Test]
    public async Task APendingFillInACompletedSettlement_IsUnbookedFills()
    {
        var (world, clientMoney) = await WorldAsync(funding: 1055.25m);
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = Feed.AddFill(id, 10m, 110m);
        await BrokerHttp.RunReportsAsync();
        Feed.AddSettled(id, fill.Id!, settlementId: "w-S1");
        Feed.AddSettlement("w-S1", clientMoney.AccountNumber, -1100m, 1);
        await BrokerHttp.RunReportsAsync();
        var settlement = (await TradeSettlementReportsTests.SettlementsAsync(world.Tenant.Id)).Single();
        await BankFeedHttp.SendAsync();
        var payment = (await TradeSettlementPaymentTests.PaymentsAsync(settlement.Id)).Single();
        BankFeedHttp.Feed.Add(clientMoney.AccountNumber, TradeSettlementPaymentTests.Debit(1100m, payment.InstructionPublicId));
        await BankFeedHttp.RunAsync();
        (await OrderHttp.OrderAsync(id)).Fills.Single().Status.ShouldBe(OrderFillStatus.Pending);

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Breaks");
        Items(run).ShouldBe([("UnbookedFills", "Break", -1100m, 1)]);
    }

    [Test]
    public async Task AnOrderWithNoSettlementAccount_StaysInTrades()
    {
        var world = await OrderWorld.CreateAsync();
        var clientMoney = await SettlementAccountTests.ClientMoneyAsync(world.Tenant.Id, "USD");
        await TradeSettlementReportsTests.FilledAsync(world, "Buy", 4m, 100.12m);

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("Trades", "Known", 405.48m, 1)]);
    }

    private static async Task<(OrderWorld World, Domain.Entities.ClientMoneyAccount ClientMoney)> WorldAsync(decimal funding)
    {
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<FakeBrokerConnections>(FunctionalTestSetup.WebApiServices).SetBankAccount("USD");
        var world = await OrderWorld.CreateAsync(funding: funding);
        return (world, await SettlementAccountTests.ClientMoneyAsync(world.Tenant.Id, "USD"));
    }
}
