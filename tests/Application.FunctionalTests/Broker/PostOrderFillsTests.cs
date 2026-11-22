using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Fees;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Broker;

/// <summary>Orders R31–R37: each fill is posted as one <c>Buy</c> or <c>Sell</c> at the broker's price, with the fee
/// of its order across its fills; a fill that cannot be posted waits and is tried again.</summary>
[Parallelizable]
public class PostOrderFillsTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    private static async Task<Transaction> TransactionAsync(int id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Include(row => row.CashLegs)
            .Include(row => row.SecurityLegs)
            .Include(row => row.FeeDetails)
            .Include(row => row.ExecutionDetail)
            .SingleAsync(row => row.Id == id);
    }

    private static async Task<JsonElement> HoldingAsync(OrderWorld world, string symbol = "ACME")
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}/holdings", world.TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("symbol").GetString() == symbol).Clone();
    }

    [Test]
    public async Task AFullFill_PostsOneBuy_AtTheBrokersPrice_WithTheOrderAsItsSource_AndItsExecutionDetail()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 100.12m, "CN-1");
        var day = OrderHttp.Today;
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Skipped, outcome.Failed).ShouldBe((1, 0, 0));
        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.ReservedCash, order.PostedGross, order.FeeCharged).ShouldBe((OrderStatus.Filled, 0m, 1001.20m, 5.01m));
        var fill = order.Fills.ShouldHaveSingleItem();
        (fill.Status, fill.HoldReason, fill.NetCash).ShouldBe((OrderFillStatus.Posted, (FillHoldReason?)null, (decimal?)-1006.21m));
        fill.PostedAt!.Value.ShouldBeGreaterThan(before);

        var buy = await TransactionAsync(fill.TransactionId!.Value);
        (buy.Type, buy.SourceKind, buy.SourcePublicId, buy.Memo, buy.Reference, buy.CreatedBy)
            .ShouldBe((TransactionType.Buy, TransactionSourceKind.OrderFill, (Guid?)id, "Buy order", (string?)null, "system"));
        buy.BookedAt.ShouldBeGreaterThan(before);
        buy.BookedAt.ShouldBe(fill.PostedAt.Value);
        buy.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(-1001.20m);
        buy.CashLegs.Single(leg => leg.Role == CashLegRole.Fee).Amount.ShouldBe(-5.01m);
        var security = buy.SecurityLegs.ShouldHaveSingleItem();
        (security.Quantity, security.CostAmount, security.CostCurrency).ShouldBe((10m, 1006.21m, "USD"));
        var detail = buy.ExecutionDetail.ShouldNotBeNull();
        (detail.FillPublicId, detail.Price, detail.TradeDate, detail.SettlementDate, detail.BrokerReference)
            .ShouldBe((fill.PublicId, 100.12m, day, day.AddDays(2), "CN-1"));

        var holding = await HoldingAsync(world);
        holding.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        holding.GetProperty("cost").GetDecimal().ShouldBe(1006.21m);
        (await world.CashAsync()).ShouldBe((3993.79m, 0m, 3993.79m));

        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        var listed = item.GetProperty("fills").EnumerateArray().Single();
        listed.GetProperty("status").GetString().ShouldBe("Posted");
        listed.GetProperty("holdReason").ValueKind.ShouldBe(JsonValueKind.Null);
        listed.GetProperty("transactionId").GetGuid().ShouldBe(buy.PublicId);
        listed.GetProperty("postedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?pendingFill=true")).ShouldBeEmpty();
    }

    [Test]
    public async Task TwoPartialFills_PayTheFeeOfOneFill_TheMinimumOnce_UnderTheOrdersSchedule()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var scheduleId = (await OrderHttp.OrderAsync(id)).TradeFeeScheduleId;

        Feed.AddFill(id, 4m, 100.12m, "CN-1");
        await BrokerHttp.RunReportsAsync();

        var afterFirst = await OrderHttp.OrderAsync(id);
        (afterFirst.Status, afterFirst.ReservedCash, afterFirst.FeeCharged).ShouldBe((OrderStatus.PartlyFilled, 630.15m, 5.00m));
        (await world.CashAsync()).ShouldBe((4594.52m, 630.15m, 3964.37m));

        Feed.AddFill(id, 6m, 100.17m, "CN-2");
        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.ReservedCash, order.PostedGross, order.FeeCharged).ShouldBe((OrderStatus.Filled, 0m, 1001.50m, 5.01m));
        var first = await TransactionAsync(order.Fills[0].TransactionId!.Value);
        var second = await TransactionAsync(order.Fills[1].TransactionId!.Value);
        var firstFee = first.FeeDetails.ShouldHaveSingleItem();
        (firstFee.TradeFeeScheduleId, firstFee.BasisAmount, firstFee.FeeAmount).ShouldBe((scheduleId, 400.48m, 5.00m));
        var secondFee = second.FeeDetails.ShouldHaveSingleItem();
        (secondFee.TradeFeeScheduleId, secondFee.BasisAmount, secondFee.FeeAmount).ShouldBe((scheduleId, 601.02m, 0.01m));
        first.SecurityLegs.Single().CostAmount.ShouldBe(405.48m);
        second.SecurityLegs.Single().CostAmount.ShouldBe(601.03m);
        (first.SourcePublicId, second.SourcePublicId).ShouldBe(((Guid?)id, (Guid?)id));
        (await world.CashAsync()).ShouldBe((3993.49m, 0m, 3993.49m));
    }

    [Test]
    public async Task AScheduleThatTakesEffectBetweenPlacementAndFill_IsNotUsed()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var placedWith = (await OrderHttp.OrderAsync(id)).TradeFeeScheduleId;
        await FeeHttp.AddScheduleAsync(world.Tenant.Id, "USD", OrderHttp.Today, 50.00m, new FeeBand(0m, 2m));
        Feed.AddFill(id, 10m, 100m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        var buy = await TransactionAsync(order.Fills.Single().TransactionId!.Value);
        var fee = buy.FeeDetails.ShouldHaveSingleItem();
        (fee.TradeFeeScheduleId, fee.FeeAmount).ShouldBe((placedWith, 5.00m));
    }

    [Test]
    public async Task AnOrderWithNoSchedule_IsFilledWithoutAFee()
    {
        var world = await OrderWorld.CreateAsync(feeSchedule: false);
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 100m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        var buy = await TransactionAsync(order.Fills.Single().TransactionId!.Value);
        buy.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-1000m);
        buy.FeeDetails.ShouldBeEmpty();
        (await world.CashAsync()).ShouldBe((4000.00m, 0m, 4000.00m));
    }

    [Test]
    public async Task ASellFill_ReleasesCostAtAverage_AndItsOrderReservesNoCash()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Sell", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 99m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.ReservedQuantity, order.ReservedCash).ShouldBe((OrderStatus.Filled, 0m, 0m));
        var fill = order.Fills.Single();
        fill.NetCash.ShouldBe(985.00m);
        var sell = await TransactionAsync(fill.TransactionId!.Value);
        (sell.Type, sell.Memo, sell.SourceKind).ShouldBe((TransactionType.Sell, "Sell order", TransactionSourceKind.OrderFill));
        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Principal).Amount.ShouldBe(990m);
        sell.CashLegs.Single(leg => leg.Role == CashLegRole.Fee).Amount.ShouldBe(-5m);
        var security = sell.SecurityLegs.Single();
        (security.Quantity, security.CostAmount).ShouldBe((-10m, -250m));
        var holding = await HoldingAsync(world);
        holding.GetProperty("quantity").GetDecimal().ShouldBe(30m);
        holding.GetProperty("cost").GetDecimal().ShouldBe(750m);
        holding.GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        (await world.CashAsync()).ShouldBe((4985.00m, 0m, 4985.00m));
    }

    [Test]
    public async Task ASellInAnotherCurrency_WaitsForItsRate_AndIsConvertedOnceTheRateIsStored()
    {
        var world = await OrderWorld.CreateAsync(feeSchedule: false);
        await FxConversionTests.DeleteRatesAsync();
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD", 10m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, kiwi, 20m, price: 10m);
        var id = await world.PlaceAsync("Sell", 5m, instrumentId: kiwi);
        await BrokerHttp.SendAsync();
        var sent = Feed.AddFill(id, 5m, 9.8m);

        var outcome = await BrokerHttp.RunReportsAsync();

        // R34, R36: the report is processed; the fill waits and keeps its order live.
        (outcome.Processed, outcome.Failed).ShouldBe((1, 0));
        (await BrokerHttp.ReportAsync(sent.Id!)).Status.ShouldBe(ExecutionReportStatus.Processed);
        var waiting = await OrderHttp.OrderAsync(id);
        (waiting.Status, waiting.ReservedQuantity).ShouldBe((OrderStatus.Filled, 5m));
        (waiting.Fills.Single().Status, waiting.Fills.Single().HoldReason)
            .ShouldBe((OrderFillStatus.Pending, (FillHoldReason?)FillHoldReason.NoFxRate));
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(0, 1, 0));
        (await OrderHttp.ListIdsAsync(world.TenantAdmin, "?pendingFill=true")).ShouldBe([id]);
        var listed = (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("fills").EnumerateArray().Single();
        listed.GetProperty("status").GetString().ShouldBe("Pending");
        listed.GetProperty("holdReason").GetString().ShouldBe("NoFxRate");
        listed.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);

        await FxConversionTests.SetRateAsync("NZD", "USD", 0.6m, OrderHttp.Today);

        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(1, 0, 0));
        var order = await OrderHttp.OrderAsync(id);
        (order.ReservedQuantity, order.Fills.Single().Status, order.Fills.Single().NetCash)
            .ShouldBe((0m, OrderFillStatus.Posted, (decimal?)29.4m));
        var sell = await TransactionAsync(order.Fills.Single().TransactionId!.Value);
        var leg = sell.CashLegs.ShouldHaveSingleItem();
        (leg.Amount, leg.Currency).ShouldBe((29.4m, "USD"));
        sell.SecurityLegs.Single().CostCurrency.ShouldBe("NZD");
    }

    [Test]
    public async Task AFill_AfterACancelledReport_AndOfADisabledCustomerInADisabledTenant_IsBooked()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddEnd(id, ExecutionReportKind.Cancelled, "Requested");
        await BrokerHttp.RunReportsAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        await TenantDisabler.DisableAsync(world.Tenant.Id);
        Feed.AddFill(id, 4m, 100.12m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.FilledQuantity, order.ReservedCash).ShouldBe((OrderStatus.Cancelled, 4m, 0m));
        order.Fills.Single().Status.ShouldBe(OrderFillStatus.Posted);
        (await TransactionAsync(order.Fills.Single().TransactionId!.Value)).CashLegs.Sum(leg => leg.Amount).ShouldBe(-405.48m);
    }

    [Test]
    public async Task AFillBeyondThePriceLimit_IsBookedWhenTheCashCoversIt()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 110m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Status, order.Fills.Single().Status, order.Fills.Single().NetCash)
            .ShouldBe((OrderStatus.Filled, OrderFillStatus.Posted, (decimal?)-1105.50m));
        (await world.CashAsync()).ShouldBe((3894.50m, 0m, 3894.50m));
    }

    [Test]
    public async Task AFillTheCashDoesNotCover_Waits_AndIsPostedByTheNextRunAfterADeposit()
    {
        var world = await OrderWorld.CreateAsync(funding: 1055.25m);
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 110m);

        var outcome = await BrokerHttp.RunReportsAsync();

        (outcome.Processed, outcome.Failed).ShouldBe((1, 0));
        var waiting = await OrderHttp.OrderAsync(id);
        (waiting.Status, waiting.ReservedCash).ShouldBe((OrderStatus.Filled, 1055.25m));
        (waiting.Fills.Single().Status, waiting.Fills.Single().HoldReason)
            .ShouldBe((OrderFillStatus.Pending, (FillHoldReason?)FillHoldReason.InsufficientCash));
        (await world.CashAsync()).ShouldBe((1055.25m, 1055.25m, 0m));
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(0, 1, 0));

        (await CustodialHttp.FundAsync(world.TenantAdmin, world.AccountId, 50.25m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(1, 0, 0));
        var order = await OrderHttp.OrderAsync(id);
        (order.ReservedCash, order.Fills.Single().Status).ShouldBe((0m, OrderFillStatus.Posted));
        (await world.CashAsync()).ShouldBe((0m, 0m, 0m));
        // A second run posts nothing twice.
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(0, 0, 0));
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(3);
    }

    [Test]
    public async Task AFill_MustLeaveTheCashOtherOrdersAndWithdrawalsReserve()
    {
        var world = await OrderWorld.CreateAsync(funding: 2500.00m);
        var first = await world.PlaceAsync("Buy", 10m);
        await world.PlaceAsync("Buy", 10m);
        await CustodialHttp.RequestWithdrawalAsync(world.AccountId, 300m);
        await BrokerHttp.SendAsync();
        // 2 500 less 1 055.25 for the other order and 300 for the withdrawal leaves 1 144.75 for this fill.
        Feed.AddFill(first, 10m, 114m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(first);
        (order.Fills.Single().Status, order.Fills.Single().HoldReason)
            .ShouldBe((OrderFillStatus.Pending, (FillHoldReason?)FillHoldReason.InsufficientCash));
        (await world.CashAsync()).ShouldBe((2500.00m, 2410.50m, 89.50m));
    }

    [Test]
    public async Task ASellFillAboveTheHolding_Waits_AndIsPostedByTheNextRunAfterAnAdjustment()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Sell", 30m);
        await BrokerHttp.SendAsync();
        // The holding shrinks under the order, as a consolidation would leave it (R50).
        await OrderHttp.ChangeHoldingAsync(world.AccountId, world.InstrumentId, -20m, -500m);
        Feed.AddFill(id, 30m, 99m);

        await BrokerHttp.RunReportsAsync();

        var waiting = await OrderHttp.OrderAsync(id);
        (waiting.Fills.Single().Status, waiting.Fills.Single().HoldReason)
            .ShouldBe((OrderFillStatus.Pending, (FillHoldReason?)FillHoldReason.InsufficientHolding));
        waiting.ReservedQuantity.ShouldBe(30m);

        var adjustment = await CustodialHttp.PostAsync(
            world.TenantAdmin,
            AdjustmentWorld.Body(world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m), reason: "ReconciliationBreak"),
            AdjustmentWorld.Path);
        adjustment.StatusCode.ShouldBe(HttpStatusCode.Created, await adjustment.Content.ReadAsStringAsync());

        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(1, 0, 0));
        var order = await OrderHttp.OrderAsync(id);
        (order.ReservedQuantity, order.Fills.Single().Status).ShouldBe((0m, OrderFillStatus.Posted));
        (await CustodialHttp.ErrorsAsync(await world.TryPlaceAsync("Sell", 1m)))["Quantity"]
            .ShouldBe(["The account has 0 ACME available to sell."]);
    }

    [Test]
    public async Task AFillOnAClosedAccount_Waits()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);
        Feed.AddFill(id, 10m, 100m);

        await BrokerHttp.RunReportsAsync();

        var order = await OrderHttp.OrderAsync(id);
        (order.Fills.Single().Status, order.Fills.Single().HoldReason)
            .ShouldBe((OrderFillStatus.Pending, (FillHoldReason?)FillHoldReason.AccountClosed));

        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Open);
        (await BrokerHttp.PostFillsAsync()).ShouldBe(new FillPostingOutcome(1, 0, 0));
    }

    [Test]
    public async Task AFill_CannotBeReversedByHand_AndTheHandTradeBeforeItIsLockedByIt()
    {
        var world = await OrderWorld.CreateAsync();
        var handBuy = await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        Feed.AddFill(id, 10m, 100m);
        await BrokerHttp.RunReportsAsync();
        var fill = (await TransactionAsync((await OrderHttp.OrderAsync(id)).Fills.Single().TransactionId!.Value)).PublicId;

        var reverseFill = await CustodialHttp.ReverseAsync(world.TenantAdmin, fill);
        var reverseHand = await CustodialHttp.ReverseAsync(world.TenantAdmin, handBuy);

        (await CustodialHttp.ErrorsAsync(reverseFill)).Values.SelectMany(messages => messages)
            .ShouldContain("This transaction was written by the platform and cannot be reversed by hand.");
        (await CustodialHttp.ErrorsAsync(reverseHand)).Values.SelectMany(messages => messages)
            .ShouldContain("A later transaction changed this holding. Reverse the later transactions first.");
    }

    [Test]
    public async Task AConnectionThatIsNotConfigured_PostsNothing()
    {
        (await BrokerHttp.PostFillsAsync("other")).ShouldBe(new FillPostingOutcome(0, 0, 0));
    }
}
