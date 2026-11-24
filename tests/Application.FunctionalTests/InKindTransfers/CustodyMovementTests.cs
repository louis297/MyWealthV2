using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R15–R19: trade deliveries, corporate action movements, movements in suspense, ignore and
/// reopen, and the movement reads.</summary>
[Parallelizable]
public class CustodyMovementTests : TestBase
{
    private const string Path = "/custody-movements";

    [Test]
    public async Task ATradeMovement_NamingABuyFillOfAnOrderOnTheAccount_IsMatchedToTheFill_AndPostsNothing()
    {
        var world = await TransferWorld.CreateAsync();
        var (_, fillReport) = await FilledAsync(world, "Buy", 10m);
        var transactions = await TransferHttp.TransactionCountAsync(world.AccountId);
        var id = AddTrade(world, CustodyMovementDirection.In, 10m, fillReport);

        await TransferHttp.PullAndProcessAsync();

        var movement = await TransferHttp.MovementAsync(id);
        (movement.Status, movement.MatchMethod).ShouldBe((CustodyMovementStatus.Matched, (CustodyMatchMethod?)CustodyMatchMethod.Trade));
        movement.OrderFillId.ShouldNotBeNull();
        (await TransferHttp.TransactionCountAsync(world.AccountId)).ShouldBe(transactions);
    }

    [Test]
    public async Task ASecondMovementForTheSameFill_AnotherQuantity_AndASellDeliveredIn_AreTradeMismatch()
    {
        var world = await TransferWorld.CreateAsync();
        var (_, buyReport) = await FilledAsync(world, "Buy", 10m);
        var (_, sellReport) = await FilledAsync(world, "Sell", 5m);
        AddTrade(world, CustodyMovementDirection.In, 10m, buyReport);
        var second = AddTrade(world, CustodyMovementDirection.In, 10m, buyReport);
        var (_, otherReport) = await FilledAsync(world, "Buy", 3m);
        var quantity = AddTrade(world, CustodyMovementDirection.In, 2m, otherReport);
        var direction = AddTrade(world, CustodyMovementDirection.In, 5m, sellReport);

        await TransferHttp.PullAndProcessAsync();

        foreach (var id in new[] { second, quantity, direction })
        {
            (await TransferHttp.MovementAsync(id)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.TradeMismatch, id);
        }
    }

    [Test]
    public async Task ATradeMovementNamingNoFill_OrNoReference_IsUnknownTrade()
    {
        var world = await TransferWorld.CreateAsync();
        var unknown = AddTrade(world, CustodyMovementDirection.In, 1m, "no-such-fill");
        var none = AddTrade(world, CustodyMovementDirection.In, 1m, null);

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.MovementAsync(unknown)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnknownTrade);
        (await TransferHttp.MovementAsync(none)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnknownTrade);
    }

    [Test]
    public async Task ATradeMovementForAnOrderWithNoSafekeepingAccount_IsUnknownTrade()
    {
        var world = await TransferWorld.CreateAsync(safekeeping: false);
        var (_, report) = await FilledAsync(world, "Buy", 10m);
        var safekeeping = await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");
        var id = TransferHttp.Feed.AddMovement(safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 10m,
            kind: CustodyMovementKind.Trade, tradeReference: report).Id!;

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.MovementAsync(id)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnknownTrade);
    }

    [Test]
    public async Task ACorporateActionMovement_NamingTheTenantsFeedAction_IsMatched_AndAnUnknownOneIsUnknownCorporateAction()
    {
        var world = await TransferWorld.CreateAsync();
        var actionId = await FeedActionAsync(world, "split-77");
        var number = world.Safekeeping.AccountNumber;
        var matched = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 100m,
            kind: CustodyMovementKind.CorporateAction, corporateActionId: "split-77").Id!;
        var unknown = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 100m,
            kind: CustodyMovementKind.CorporateAction, corporateActionId: "split-99").Id!;

        await TransferHttp.PullAndProcessAsync();

        var movement = await TransferHttp.MovementAsync(matched);
        (movement.MatchMethod, movement.CorporateActionId).ShouldBe(((CustodyMatchMethod?)CustodyMatchMethod.CorporateAction, (int?)actionId));
        (await TransferHttp.MovementAsync(unknown)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnknownCorporateAction);
    }

    [Test]
    public async Task ATransferWithNoInstruction_IsNoInstruction_AndNoKindIsUnsupportedKind()
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        var transfer = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 800m).Id!;
        var noKind = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m, kind: null).Id!;

        await TransferHttp.PullAndProcessAsync();

        (await TransferHttp.MovementAsync(transfer)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.NoInstruction);
        (await TransferHttp.MovementAsync(noKind)).UnmatchedReason.ShouldBe(CustodyUnmatchedReason.UnsupportedKind);
    }

    [Test]
    public async Task Ignore_NeedsANote_ThenIsIgnoredWithItsReason_AndReopenMakesItUnmatchedAgain()
    {
        var world = await TransferWorld.CreateAsync();
        var outsideId = TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 800m).Id!;
        await TransferHttp.PullAndProcessAsync();
        var id = await MovementPublicIdAsync(outsideId);

        (await CustodialHttp.ErrorsAsync(await ActAsync(world.TenantAdmin, id, "ignore"))).ShouldContainKey("Note");
        (await ActAsync(world.TenantAdmin, id, "ignore", "Bulk delivery of the migration batch")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Ignored");
        item.GetProperty("unmatchedReason").GetString().ShouldBe("NoInstruction");
        item.GetProperty("lastAction").GetProperty("action").GetString().ShouldBe("Ignore");
        item.GetProperty("lastAction").GetProperty("note").GetString().ShouldBe("Bulk delivery of the migration batch");

        (await ActAsync(world.TenantAdmin, id, "reopen")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        item = await GetAsync(world.TenantAdmin, id);
        (item.GetProperty("status").GetString(), item.GetProperty("unmatchedReason").GetString()).ShouldBe(("Unmatched", "NoInstruction"));
        item.GetProperty("lastAction").GetProperty("action").GetString().ShouldBe("Reopen");
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.TenantAdmin, id, "reopen")))["Status"]
            .ShouldBe(["This movement is Unmatched and cannot be reopened."]);
    }

    [Test]
    public async Task IgnoringAMatchedMovement_Returns400AtStatus_AndAStaleRowVersionIs409()
    {
        var world = await TransferWorld.CreateAsync();
        var transfer = await world.SentAsync();
        var outsideId = world.AddConfirmation(transfer);
        await TransferHttp.PullAndProcessAsync();
        var id = await MovementPublicIdAsync(outsideId);

        (await CustodialHttp.ErrorsAsync(await ActAsync(world.TenantAdmin, id, "ignore", "Note")))["Status"]
            .ShouldBe(["This movement is Matched and cannot be ignored."]);
        var stale = await TransferHttp.Send(HttpMethod.Post, $"{Path}/{id}/ignore", world.TenantAdmin,
            new { rowVersion = TransferHttp.StaleRowVersion, note = "Note" });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustodialHttp.ErrorsAsync(await TransferHttp.Send(HttpMethod.Post, $"{Path}/{id}/ignore", world.TenantAdmin,
            new { note = "Note" }))).ShouldContainKey("RowVersion");
    }

    [Test]
    public async Task TheItem_CarriesTheMovementsFields_AndTheLinksOfItsMatch()
    {
        var world = await TransferWorld.CreateAsync();
        var transfer = await world.SentAsync();
        var outsideId = world.AddConfirmation(transfer);
        await TransferHttp.PullAndProcessAsync();
        var id = await MovementPublicIdAsync(outsideId);

        var item = await GetAsync(world.TenantAdmin, id);

        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("safekeepingAccountId").GetGuid().ShouldBe(world.Safekeeping.PublicId);
        (item.GetProperty("direction").GetString(), item.GetProperty("kind").GetString()).ShouldBe(("In", "Transfer"));
        (item.GetProperty("symbol").GetString(), item.GetProperty("currency").GetString()).ShouldBe(("ACME", "USD"));
        item.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        item.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        item.GetProperty("instructionId").GetString().ShouldBe(transfer.ToString());
        item.GetProperty("counterpartyName").GetString().ShouldBe("Harbour Brokers");
        (item.GetProperty("status").GetString(), item.GetProperty("method").GetString()).ShouldBe(("Matched", "Transfer"));
        item.GetProperty("unmatchedReason").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("inKindTransferId").GetGuid().ShouldBe(transfer);
        item.GetProperty("transactionId").GetGuid()
            .ShouldBe((await TransferHttp.GetAsync(world.TenantAdmin, transfer)).GetProperty("transactionId").GetGuid());
        item.GetProperty("orderId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("lastAction").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task TheList_IsNewestFirst_AndFilters()
    {
        var world = await TransferWorld.CreateAsync();
        var number = world.Safekeeping.AccountNumber;
        var first = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.In, "ACME", 1m).Id!;
        await TransferHttp.PullAndProcessAsync();
        var second = TransferHttp.Feed.AddMovement(number, CustodyMovementDirection.Out, "OTHER", 1m, kind: null).Id!;
        await TransferHttp.PullAndProcessAsync();
        var (firstId, secondId) = (await MovementPublicIdAsync(first), await MovementPublicIdAsync(second));

        (await ListAsync(world.TenantAdmin)).ShouldBe([secondId, firstId]);
        (await ListAsync(world.TenantAdmin, "?status=Unmatched,Ignored")).ShouldBe([secondId, firstId]);
        (await ListAsync(world.TenantAdmin, "?status=Matched")).ShouldBeEmpty();
        (await ListAsync(world.TenantAdmin, "?direction=Out")).ShouldBe([secondId]);
        (await ListAsync(world.TenantAdmin, "?kind=Transfer")).ShouldBe([firstId]);
        (await ListAsync(world.TenantAdmin, $"?instrumentId={world.InstrumentId}")).ShouldBe([firstId]);
        (await ListAsync(world.TenantAdmin, $"?instrumentId={Guid.NewGuid()}")).ShouldBeEmpty();
        (await ListAsync(world.TenantAdmin, $"?safekeepingAccountId={world.Safekeeping.PublicId}")).ShouldBe([secondId, firstId]);
        (await CustodialHttp.ErrorsAsync(await TransferHttp.Send(HttpMethod.Get, $"{Path}?status=Gone", world.TenantAdmin)))
            .ShouldContainKey("Status");
        (await TransferHttp.Send(HttpMethod.Get, $"{Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task EveryRoute_Is401WithoutBearer_And403ForAnAdviserAndACustomer()
    {
        var world = await TransferWorld.CreateAsync();
        var outsideId = TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 1m).Id!;
        await TransferHttp.PullAndProcessAsync();
        var id = await MovementPublicIdAsync(outsideId);
        var routes = new[]
        {
            (HttpMethod.Get, Path), (HttpMethod.Get, $"{Path}/{id}"), (HttpMethod.Post, $"{Path}/{id}/ignore"),
            (HttpMethod.Post, $"{Path}/{id}/reopen")
        };

        foreach (var (method, route) in routes)
        {
            var body = new { rowVersion = TransferHttp.StaleRowVersion, note = "Note" };
            (await TransferHttp.Send(method, route, null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);
            (await TransferHttp.Send(method, route, world.Adviser, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
            (await TransferHttp.Send(method, route, world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
        }
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsMovement_Returns404_AndIsNotListed()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var outsideId = TransferHttp.Feed.AddMovement(south.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 1m).Id!;
        await TransferHttp.PullAndProcessAsync();
        var id = await MovementPublicIdAsync(outsideId);
        var rowVersion = (await GetAsync(south.TenantAdmin, id)).GetProperty("rowVersion").GetString();

        (await TransferHttp.Send(HttpMethod.Get, $"{Path}/{id}", north.TenantAdmin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransferHttp.Send(HttpMethod.Post, $"{Path}/{id}/ignore", north.TenantAdmin, new { rowVersion, note = "Note" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(north.TenantAdmin)).ShouldBeEmpty();
    }

    /// <summary>Places an order as the Adviser and books one full fill of it; returns the fill report's outside id.</summary>
    private static async Task<(Guid Order, string FillReport)> FilledAsync(TransferWorld world, string side, decimal quantity)
    {
        var order = await world.Orders.PlaceAsync(side, quantity);
        await BrokerHttp.SendAsync();
        var fill = BrokerHttp.Feed.AddFill(order, quantity, 100m);
        await BrokerHttp.RunReportsAsync();
        return (order, fill.Id!);
    }

    private static string AddTrade(TransferWorld world, CustodyMovementDirection direction, decimal quantity, string? report) =>
        TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, direction, "ACME", quantity, kind: CustodyMovementKind.Trade,
            tradeReference: report).Id!;

    /// <summary>A <c>Feed</c> corporate action of the tenant's ACME with the party's action id, written straight to the
    /// database as the corporate action feed would.</summary>
    private static async Task<int> FeedActionAsync(TransferWorld world, string partyActionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == world.InstrumentId);
        var action = CorporateAction.Announce(world.Tenant.Id, instrument.Id, "simulator", partyActionId, 1, CorporateActionKind.Split,
            new Domain.CorporateActions.CorporateActionRatio(2, 1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), null, null);
        action.CreatedBy = "test";
        db.CorporateActions.Add(action);
        await db.SaveChangesAsync();
        return action.Id;
    }

    private static async Task<Guid> MovementPublicIdAsync(string outsideId) => (await TransferHttp.MovementAsync(outsideId)).PublicId;

    private static async Task<JsonElement> GetAsync(string token, Guid id)
    {
        var response = await TransferHttp.Send(HttpMethod.Get, $"{Path}/{id}", token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static async Task<List<Guid>> ListAsync(string token, string query = "")
    {
        var response = await TransferHttp.Send(HttpMethod.Get, Path + query, token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<HttpResponseMessage> ActAsync(string token, Guid id, string action, string? note = null)
    {
        var rowVersion = (await GetAsync(token, id)).GetProperty("rowVersion").GetString();
        return await TransferHttp.Send(HttpMethod.Post, $"{Path}/{id}/{action}", token, new { rowVersion, note });
    }
}
