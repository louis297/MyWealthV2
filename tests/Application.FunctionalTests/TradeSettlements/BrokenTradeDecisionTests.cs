using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.TradeSettlements;

/// <summary>Trade-settlement R30, R31, R33, R34: a break the flow cannot reverse waits for a TenantAdmin, who
/// resolves it by hand; the cash, the closure and the items around it.</summary>
[Parallelizable]
public class BrokenTradeDecisionTests : TestBase
{
    private static InMemoryBrokerFeed Feed => BrokerHttp.Feed;

    [Test]
    public async Task AFillWithLaterActivityOnItsHolding_IsNotApplied_NothingIsPosted_AndASecondRunPostsNothing()
    {
        var world = await OrderWorld.CreateAsync();
        var (first, firstFill) = await BrokenTradeTests.FilledAsync(world, 4m);
        await BrokenTradeTests.FilledAsync(world, 2m);
        Feed.AddBroken(first, firstFill);

        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.RunReportsAsync();
        await BrokerHttp.PostFillsAsync();

        var fill = (await OrderHttp.OrderAsync(first)).Fills.Single();
        (fill.Status, fill.BreakOutcome, fill.BreakHoldReason, fill.ReversalTransactionId).ShouldBe((
            OrderFillStatus.Broken, (FillBreakOutcome?)FillBreakOutcome.NotApplied,
            (FillBreakHoldReason?)FillBreakHoldReason.LaterActivity, (int?)null));
        (await ReversalsAsync(world)).ShouldBeEmpty();
    }

    [Test]
    public async Task ABrokenSellWhoseProceedsWereSpent_IsNotAppliedAsInsufficientCash_AndStaysUnsettledCash()
    {
        var (world, sell, _) = await SpentSaleAsync();

        var fill = (await OrderHttp.OrderAsync(sell)).Fills.Single();
        (fill.BreakOutcome, fill.BreakHoldReason).ShouldBe((
            (FillBreakOutcome?)FillBreakOutcome.NotApplied, (FillBreakHoldReason?)FillBreakHoldReason.InsufficientCash));
        (await UnsettledAsync(world)).ShouldBe(985.00m);
        (await ReversalsAsync(world)).ShouldBeEmpty();
    }

    [Test]
    public async Task AFillOnAClosedAccount_IsNotAppliedAsAccountClosed()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fill) = await BrokenTradeTests.FilledAsync(world, 4m);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == world.AccountId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, AccountStatus.Closed));
        }

        Feed.AddBroken(id, fill);
        await BrokerHttp.RunReportsAsync();

        (await OrderHttp.OrderAsync(id)).Fills.Single().BreakHoldReason.ShouldBe(FillBreakHoldReason.AccountClosed);
    }

    [Test]
    public async Task Resolve_AsTenantAdmin_Returns204_AndRecordsThePersonTheTimeAndTheNote_WithNoLedgerWrite()
    {
        var (world, sell, _) = await SpentSaleAsync();
        var order = await OrderHttp.OrderAsync(sell);
        var cashBefore = await world.CashAsync();

        var response = await ResolveAsync(world.TenantAdmin, order, "  Corrected with adjustment 2026-11-24  ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var fill = (await OrderHttp.OrderAsync(sell)).Fills.Single();
        (fill.BreakOutcome, fill.BreakNote, fill.BreakResolvedAt is not null, fill.BreakResolvedByUserId is not null)
            .ShouldBe(((FillBreakOutcome?)FillBreakOutcome.ResolvedByHand, "Corrected with adjustment 2026-11-24", true, true));
        (await world.CashAsync()).ShouldBe(cashBefore);
        (await UnsettledAsync(world)).ShouldBe(0m);
        var item = await OrderHttp.GetAsync(world.TenantAdmin, sell);
        var broken = item.GetProperty("fills")[0].GetProperty("break");
        broken.GetProperty("outcome").GetString().ShouldBe("ResolvedByHand");
        broken.GetProperty("resolvedBy").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Resolve_AsAdviser_Returns403_WithNoWaitingBreak_Returns400_AndANoteIsRequired()
    {
        var (world, sell, buy) = await SpentSaleAsync();
        var order = await OrderHttp.OrderAsync(sell);

        var adviser = await ResolveAsync(world.Adviser, order, "Note");
        var noBreak = await ResolveAsync(world.TenantAdmin, await OrderHttp.OrderAsync(buy), "Note");
        var noNote = await ResolveAsync(world.TenantAdmin, order, "   ");

        adviser.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CustodialHttp.ErrorsAsync(noBreak))["Status"].ShouldBe(["This trade has no break that waits for a decision."]);
        (await CustodialHttp.ErrorsAsync(noNote))["Note"].ShouldBe(["Note must be 1 to 200 characters."]);
    }

    [Test]
    public async Task Resolve_OfAnotherTenantsOrder_OrAnUnknownFill_Returns404_AndAStaleRowVersion_Returns409()
    {
        var (world, sell, _) = await SpentSaleAsync();
        var order = await OrderHttp.OrderAsync(sell);
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");

        var otherTenant = await ResolveAsync(south.TenantAdmin, order, "Note");
        var unknownFill = await TenantHttp.Send(
            HttpMethod.Post, $"/orders/{order.PublicId}/fills/{Guid.NewGuid()}/resolve-break", world.TenantAdmin,
            new { rowVersion = Convert.ToBase64String(order.RowVersion), note = "Note" });
        var stale = await TenantHttp.Send(
            HttpMethod.Post, $"/orders/{order.PublicId}/fills/{order.Fills.Single().PublicId}/resolve-break", world.TenantAdmin,
            new { rowVersion = "AAAAAAAAAAE=", note = "Note" });

        otherTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownFill.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task AWaitingBreak_RefusesTheAccountClosure_AndIsAPendingFillOrder()
    {
        var (world, sell, _) = await SpentSaleAsync();

        var closure = await CustodialHttp.CloseAsync(world.TenantAdmin, world.AccountId);
        var pending = await TenantHttp.Send(HttpMethod.Get, $"/orders?pendingFill=true", world.TenantAdmin);

        (await CustodialHttp.ErrorsAsync(closure))["AccountId"].ShouldBe(["A broken trade on this account waits for a decision."]);
        using var json = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldContain(sell);
    }

    [Test]
    public async Task TheFillItem_ShowsTheBreak_AndTheCustomerReadsItWithoutTheNote_AndTheTransactionHasBrokenAt()
    {
        var world = await OrderWorld.CreateAsync();
        var (id, fillReport) = await BrokenTradeTests.FilledAsync(world, 4m);
        Feed.AddBroken(id, fillReport);
        await BrokerHttp.RunReportsAsync();
        var stored = (await OrderHttp.OrderAsync(id)).Fills.Single();

        var adviser = (await OrderHttp.GetAsync(world.Adviser, id)).GetProperty("fills")[0];
        var customer = (await OrderHttp.GetAsync(world.CustomerToken, id)).GetProperty("fills")[0];

        adviser.GetProperty("status").GetString().ShouldBe("Broken");
        var broken = adviser.GetProperty("break");
        broken.EnumerateObject().Select(property => property.Name).ShouldBe([
            "at", "reason", "outcome", "holdReason", "reversalTransactionId", "resolvedBy", "resolvedAt", "note"
        ], ignoreOrder: true);
        (broken.GetProperty("reason").GetString(), broken.GetProperty("outcome").GetString())
            .ShouldBe(("Broken by the exchange", "Reversed"));
        var reversalId = broken.GetProperty("reversalTransactionId").GetGuid();
        adviser.TryGetProperty("brokerSettlementId", out _).ShouldBeTrue();
        adviser.TryGetProperty("tradeSettlementId", out _).ShouldBeTrue();
        customer.GetProperty("break").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        customer.GetProperty("tradeSettlementId").ValueKind.ShouldBe(JsonValueKind.Null);
        using var transaction = await CustodialHttp.GetTransactionAsync(world.CustomerToken, adviser.GetProperty("transactionId").GetGuid());
        transaction.RootElement.GetProperty("execution").GetProperty("brokenAt").GetDateTimeOffset()
            .ShouldBe(stored.BrokenAt!.Value, TimeSpan.FromSeconds(1));

        var reverse = await CustodialHttp.ReverseAsync(world.TenantAdmin, reversalId);
        reverse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task InTheReconciliation_AWaitingBreak_IsTradesNeedingAction_AndAResolvedOne_IsTrades()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (first, firstFill) = await BrokenTradeTests.FilledAsync(world, 4m);
        await BrokenTradeTests.FilledAsync(world, 2m);
        Feed.AddBroken(first, firstFill);
        await BrokerHttp.RunReportsAsync();

        var waiting = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);
        (await ResolveAsync(world.TenantAdmin, await OrderHttp.OrderAsync(first), "Corrected")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var resolved = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Items(waiting).ShouldContain(("TradesNeedingAction", "Break", 400.48m, 1));
        Items(resolved).ShouldNotContain(item => item.Kind == "TradesNeedingAction");
        Items(resolved).ShouldContain(("Trades", "Known", 405.48m, 1));
    }

    [Test]
    public async Task InTheReconciliation_AReversedBrokenFill_AddsNothing()
    {
        var (world, clientMoney) = await TradeSettlementReportsTests.WorldAsync();
        var (id, fill) = await BrokenTradeTests.FilledAsync(world, 4m);
        Feed.AddBroken(id, fill);
        await BrokerHttp.RunReportsAsync();

        var run = await RunAndGetAsync(world.TenantAdmin, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBeEmpty();
    }

    /// <summary>40 ACME typed in; a sell of 10 at 99.00 nets 985.00; a buy of 9 BETA at 100.00 spends 905.00 of it;
    /// then the broker breaks the sell.</summary>
    private static async Task<(OrderWorld World, Guid Sell, Guid Buy)> SpentSaleAsync()
    {
        var world = await OrderWorld.CreateAsync(funding: 0m);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var sell = await world.PlaceAsync("Sell", 10m);
        await BrokerHttp.SendAsync();
        var sold = Feed.AddFill(sell, 10m, 99m);
        await BrokerHttp.RunReportsAsync();
        var beta = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "BETA", "USD", 100m);
        var buy = await world.PlaceAsync("Buy", 9m, instrumentId: beta);
        await BrokerHttp.SendAsync();
        Feed.AddFill(buy, 9m, 100m);
        await BrokerHttp.RunReportsAsync();
        Feed.AddBroken(sell, sold.Id!);
        await BrokerHttp.RunReportsAsync();
        return (world, sell, buy);
    }

    private static Task<HttpResponseMessage> ResolveAsync(string accessToken, Order order, string note) =>
        TenantHttp.Send(
            HttpMethod.Post, $"/orders/{order.PublicId}/fills/{order.Fills.Single().PublicId}/resolve-break", accessToken,
            new { rowVersion = Convert.ToBase64String(order.RowVersion), note });

    private static async Task<List<Transaction>> ReversalsAsync(OrderWorld world)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == world.Tenant.Id && row.Type == TransactionType.Reversal)
            .ToListAsync();
    }

    private static async Task<decimal> UnsettledAsync(OrderWorld world)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}", world.TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("unsettledCash").GetDecimal();
    }
}
