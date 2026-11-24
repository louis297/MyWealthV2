using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.FunctionalTests.Broker;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R50–R59: "Run now", what a run shows, and the reads.</summary>
[Parallelizable]
public class CustodianReconciliationRunTests : TestBase
{
    private const string Path = "/custodian-reconciliations";

    [Test]
    public async Task EveryRoute_Is401WithoutBearer_And403ForAnAdviserACustomerAndASystemAdmin()
    {
        var world = await TransferWorld.CreateAsync();
        var system = await TenantHttp.SignInSystemAdmin();
        var routes = new[] { (HttpMethod.Get, $"{Path}/overview"), (HttpMethod.Get, Path), (HttpMethod.Get, $"{Path}/{Guid.NewGuid()}"), (HttpMethod.Post, Path) };

        foreach (var (method, route) in routes)
        {
            var body = new { safekeepingAccountId = world.Safekeeping.PublicId };
            (await TransferHttp.Send(method, route, null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);
            foreach (var token in new[] { world.Adviser, world.CustomerToken, system.AccessToken })
            {
                (await TransferHttp.Send(method, route, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
            }
        }
    }

    [Test]
    public async Task RunNow_WithNoStoredPositions_IsNoPositions()
    {
        var world = await TransferWorld.CreateAsync();

        var run = await RunAsync(world);

        run.GetProperty("status").GetString().ShouldBe("NoPositions");
        run.GetProperty("trigger").GetString().ShouldBe("Manual");
        run.GetProperty("ranBy").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        run.GetProperty("positionsAt").ValueKind.ShouldBe(JsonValueKind.Null);
        run.GetProperty("lines").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task HoldingsEqualToThePositions_AreAgreed()
    {
        var world = await TransferWorld.CreateAsync();
        Deliver(world, 100m);
        await TransferHttp.PullAndProcessAsync();

        var run = await RunAsync(world);

        run.GetProperty("status").GetString().ShouldBe("Agreed");
        var line = run.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        (line.GetProperty("custodianQuantity").GetDecimal(), line.GetProperty("platformQuantity").GetDecimal()).ShouldBe((100m, 100m));
        line.GetProperty("class").GetString().ShouldBe("Agreed");
        run.GetProperty("agreedCount").GetInt32().ShouldBe(1);
        run.GetProperty("safekeepingAccount").GetProperty("accountNumber").GetString().ShouldBe(world.Safekeeping.AccountNumber);
        run.GetProperty("feed").GetProperty("lastSucceededAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task AHoldingTheCustodianDoesNotHold_IsABreak_Unexplained()
    {
        var world = await TransferWorld.CreateAsync();
        await TransferHttp.PullAndProcessAsync();

        var run = await RunAsync(world);

        run.GetProperty("status").GetString().ShouldBe("Breaks");
        var line = run.GetProperty("lines").EnumerateArray().Single();
        (line.GetProperty("difference").GetDecimal(), line.GetProperty("unexplained").GetDecimal()).ShouldBe((-100m, -100m));
        line.GetProperty("class").GetString().ShouldBe("Break");
    }

    [Test]
    public async Task APostedBuyFillNotDelivered_IsTiming_AndAgreedOnceItsTradeMovementArrives()
    {
        var world = await TransferWorld.CreateAsync();
        Deliver(world, 100m);
        var order = await world.Orders.PlaceAsync("Buy", 10m);
        await BrokerHttp.SendAsync();
        var fill = BrokerHttp.Feed.AddFill(order, 10m, 100m);
        await BrokerHttp.RunReportsAsync();
        await TransferHttp.PullAndProcessAsync();

        var timing = await RunAsync(world);
        TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 10m,
            kind: CustodyMovementKind.Trade, tradeReference: fill.Id);
        await TransferHttp.PullAndProcessAsync();
        var agreed = await RunAsync(world);

        var line = timing.GetProperty("lines").EnumerateArray().Single();
        (line.GetProperty("unsettledTrades").GetDecimal(), line.GetProperty("class").GetString()).ShouldBe((-10m, "Timing"));
        timing.GetProperty("status").GetString().ShouldBe("Agreed");
        timing.GetProperty("fillsNotDelivered").GetInt32().ShouldBe(1);
        agreed.GetProperty("lines").EnumerateArray().Single().GetProperty("class").GetString().ShouldBe("Agreed");
        agreed.GetProperty("fillsNotDelivered").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task AMovementInSuspense_IsABreak_AnAdjustmentLeavesIt_AndIgnoringItAgrees()
    {
        var world = await TransferWorld.CreateAsync();
        Deliver(world, 100m);
        var outsideId = TransferHttp.Feed.AddMovement(world.Safekeeping.AccountNumber, CustodyMovementDirection.In, "ACME", 50m).Id!;
        await TransferHttp.PullAndProcessAsync();

        var suspense = Line(await RunAsync(world));
        var adjustment = await CustodialHttp.PostAsync(world.TenantAdmin,
            AdjustmentWorld.Body(world.AccountId, reason: "ReconciliationBreak", security: AdjustmentWorld.Holding(world.InstrumentId, 50m, 1250m)),
            AdjustmentWorld.Path);
        adjustment.StatusCode.ShouldBe(HttpStatusCode.Created, await adjustment.Content.ReadAsStringAsync());
        var adjusted = Line(await RunAsync(world));
        var movement = await TransferHttp.MovementAsync(outsideId);
        var rowVersion = Convert.ToBase64String(movement.RowVersion);
        (await TransferHttp.Send(HttpMethod.Post, $"/custody-movements/{movement.PublicId}/ignore", world.TenantAdmin,
            new { rowVersion, note = "Delivered for the adjustment" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var ignored = await RunAsync(world);

        (suspense.GetProperty("suspense").GetDecimal(), suspense.GetProperty("class").GetString()).ShouldBe((50m, "Break"));
        (adjusted.GetProperty("suspense").GetDecimal(), adjusted.GetProperty("unexplained").GetDecimal()).ShouldBe((50m, -50m));
        ignored.GetProperty("status").GetString().ShouldBe("Agreed");
    }

    [Test]
    public async Task APositionInASymbolTheTenantDoesNotList_IsABreakLineWithNoInstrument()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);
        TransferHttp.Feed.Omit(world.Safekeeping.AccountNumber, new CustodianFeedMovement("bulk-other", CustodyMovementDirection.In,
            CustodyMovementKind.Transfer, "OTHER", "USD", 3m, DateOnly.FromDateTime(DateTime.UtcNow)));
        await TransferHttp.PullAndProcessAsync();

        var line = Line(await RunAsync(world));

        line.GetProperty("instrument").ValueKind.ShouldBe(JsonValueKind.Null);
        (line.GetProperty("symbol").GetString(), line.GetProperty("unexplained").GetDecimal()).ShouldBe(("OTHER", 3m));
    }

    [Test]
    public async Task ACompletedTransferIn_LeavesNoDifference()
    {
        var world = await TransferWorld.CreateAsync(holding: 0m);
        var id = await world.SentAsync();
        world.AddConfirmation(id);
        await TransferHttp.PullAndProcessAsync();

        var run = await RunAsync(world);

        run.GetProperty("status").GetString().ShouldBe("Agreed");
        Line(run).GetProperty("platformQuantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task ATransferInFlight_IsInformation()
    {
        var world = await TransferWorld.CreateAsync();
        Deliver(world, 100m);
        await world.SentAsync();
        await TransferHttp.PullAndProcessAsync();

        (await RunAsync(world)).GetProperty("transfersInFlight").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task AHoldingQuotedInAnotherCurrency_IsNotInTheRun()
    {
        var world = await TransferWorld.CreateAsync();
        var kiwi = await OrderHttp.CreateInstrumentAsync(world.TenantAdmin, "KIWI", "NZD");
        await OrderHttp.InsertHoldingAsync(world.AccountId, kiwi, 5m, 1m);
        Deliver(world, 100m);
        await TransferHttp.PullAndProcessAsync();

        var run = await RunAsync(world);

        run.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("symbol").GetString()).ShouldBe(["ACME"]);
    }

    [Test]
    public async Task RunNow_OnAnInactiveSafekeepingAccount_Returns400_AndOnAnotherTenantsReturns404()
    {
        var world = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.SafekeepingAccounts.IgnoreQueryFilters().SingleAsync(row => row.Id == world.Safekeeping.Id)).Disable();
            await db.SaveChangesAsync();
        }

        var inactive = await TransferHttp.Send(HttpMethod.Post, Path, world.TenantAdmin, new { safekeepingAccountId = world.Safekeeping.PublicId });
        var other = await TransferHttp.Send(HttpMethod.Post, Path, world.TenantAdmin, new { safekeepingAccountId = south.Safekeeping.PublicId });

        (await CustodialHttp.ErrorsAsync(inactive))["SafekeepingAccountId"].ShouldBe(["Only an active safekeeping account can be reconciled."]);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransferHttp.Send(HttpMethod.Post, Path, world.TenantAdmin,
            new { safekeepingAccountId = world.Safekeeping.PublicId, tenantId = world.Tenant.PublicId })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheOverview_ListsTheActiveAccountWithItsLatestRun_AndTheListFilters()
    {
        var world = await TransferWorld.CreateAsync();
        var first = await RunAsync(world);
        Deliver(world, 100m);
        await TransferHttp.PullAndProcessAsync();
        var second = await RunAsync(world);

        var overview = await ReadAsync(world.TenantAdmin, $"{Path}/overview");
        var entry = overview.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("safekeepingAccount").GetProperty("id").GetGuid().ShouldBe(world.Safekeeping.PublicId);
        entry.GetProperty("custodianFeed").GetProperty("lastSucceededAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        entry.GetProperty("latest").GetProperty("id").GetGuid().ShouldBe(second.GetProperty("id").GetGuid());
        entry.GetProperty("latest").TryGetProperty("lines", out _).ShouldBeFalse();

        (await ListAsync(world.TenantAdmin, "")).ShouldBe([second.GetProperty("id").GetGuid(), first.GetProperty("id").GetGuid()]);
        (await ListAsync(world.TenantAdmin, "?status=NoPositions")).ShouldBe([first.GetProperty("id").GetGuid()]);
        (await ListAsync(world.TenantAdmin, $"?safekeepingAccountId={world.Safekeeping.PublicId}")).Count.ShouldBe(2);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await ListAsync(world.TenantAdmin, $"?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).Count.ShouldBe(2);
        (await ListAsync(world.TenantAdmin, $"?to={today.AddDays(-1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await CustodialHttp.ErrorsAsync(await TransferHttp.Send(HttpMethod.Get, $"{Path}?status=Gone", world.TenantAdmin)))
            .ShouldContainKey("Status");
    }

    [Test]
    public async Task AStoredRun_DoesNotChangeWhenTheLedgerDoes()
    {
        var world = await TransferWorld.CreateAsync();
        Deliver(world, 100m);
        await TransferHttp.PullAndProcessAsync();
        var run = await RunAsync(world);
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 0.5m);

        var again = await ReadAsync(world.TenantAdmin, $"{Path}/{run.GetProperty("id").GetGuid()}");

        again.GetProperty("status").GetString().ShouldBe("Agreed");
        Line(again).GetProperty("platformQuantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsRun_Returns404_AndIsNotListed()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var run = await RunAsync(south);

        (await TransferHttp.Send(HttpMethod.Get, $"{Path}/{run.GetProperty("id").GetGuid()}", north.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(north.TenantAdmin, "")).ShouldBeEmpty();
    }

    /// <summary>The previous provider's bulk delivery of <paramref name="quantity"/> ACME, in the custodian's books but
    /// left out of the feed, so the positions cover the hand-bought holding.</summary>
    private static void Deliver(TransferWorld world, decimal quantity) =>
        TransferHttp.Feed.Omit(world.Safekeeping.AccountNumber, new CustodianFeedMovement("bulk-" + Guid.NewGuid().ToString("N"),
            CustodyMovementDirection.In, CustodyMovementKind.Transfer, "ACME", "USD", quantity, DateOnly.FromDateTime(DateTime.UtcNow)));

    private static JsonElement Line(JsonElement run) => run.GetProperty("lines").EnumerateArray().Single();

    private static async Task<JsonElement> RunAsync(TransferWorld world)
    {
        var response = await TransferHttp.Send(HttpMethod.Post, Path, world.TenantAdmin, new { safekeepingAccountId = world.Safekeeping.PublicId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"{Path}/{id}");
        return await ReadAsync(world.TenantAdmin, $"{Path}/{id}");
    }

    private static async Task<JsonElement> ReadAsync(string token, string route)
    {
        var response = await TransferHttp.Send(HttpMethod.Get, route, token);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    private static async Task<List<Guid>> ListAsync(string token, string query)
    {
        var json = await ReadAsync(token, Path + query);
        return json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }
}
