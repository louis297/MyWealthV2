using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>Orders R18: cancel an order.</summary>
[Parallelizable]
public class CancelOrderTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_AndASystemAdminOrACustomer_Returns403()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        var system = await AccountHttp.SignInSystemAdmin();
        var body = new { rowVersion = OrderHttp.StaleRowVersion };

        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Placed");
    }

    [Test]
    public async Task AnOrderThatWasNotSent_IsCancelledInTheRequest_AndItsReservationIsReleased()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await OrderHttp.CancelAsync(world.Adviser, id, "  Customer changed their mind ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(0m);
        item.GetProperty("closedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("closeReason").ValueKind.ShouldBe(JsonValueKind.Null);
        var request = item.GetProperty("cancelRequest");
        request.GetProperty("by").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        request.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
        request.GetProperty("note").GetString().ShouldBe("Customer changed their mind");
        (await world.CashAsync()).ShouldBe((5000.00m, 0m, 5000.00m));
    }

    [Test]
    public async Task AnUnsentSell_ReleasesItsQuantity()
    {
        var world = await OrderWorld.CreateAsync();
        await OrderHttp.InsertHoldingAsync(world.AccountId, world.InstrumentId, 40m);
        var id = await world.PlaceAsync("Sell", 40m);

        (await OrderHttp.CancelAsync(world.TenantAdmin, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        (await world.TryPlaceAsync("Sell", 40m)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task AnOrderTheBrokerMayHold_GetsACancelRequest_AndKeepsItsStatusAndItsReservation()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await OrderHttp.RecordAttemptAsync(id);

        var response = await OrderHttp.CancelAsync(world.Adviser, id, "Wrong quantity");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await OrderHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Placed");
        item.GetProperty("closedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("reservedCash").GetDecimal().ShouldBe(1055.25m);
        var request = item.GetProperty("cancelRequest");
        request.GetProperty("by").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        request.GetProperty("note").GetString().ShouldBe("Wrong quantity");
        (await world.CashAsync()).Reserved.ShouldBe(1055.25m);
        (await OrderHttp.OrderAsync(id)).CancelAnsweredAt.ShouldBeNull();
    }

    [Test]
    public async Task ASecondCancel_OfAnOrderWithACancelRequest_Returns400AtStatus()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await OrderHttp.RecordAttemptAsync(id);
        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await OrderHttp.CancelAsync(world.TenantAdmin, id);

        (await CustodialHttp.ErrorsAsync(again))["Status"].ShouldBe(["A cancellation was already requested for this order."]);
    }

    [Test]
    public async Task ACancel_OfAnOrderThatIsNotOpen_Returns400AtStatus()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        (await OrderHttp.CancelAsync(world.Adviser, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await OrderHttp.CancelAsync(world.Adviser, id);

        (await CustodialHttp.ErrorsAsync(again))["Status"].ShouldBe(["This order is Cancelled and cannot be cancelled."]);
    }

    [Test]
    public async Task ACancel_WithAStaleRowVersion_Returns409_AndWithNone_Returns400_AndChangesNothing()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);

        var stale = await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", world.Adviser, new { rowVersion = OrderHttp.StaleRowVersion });
        var missing = await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", world.Adviser, new { note = "No version" });

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
        (await CustodialHttp.ErrorsAsync(missing)).Keys.ShouldContain("RowVersion");
        (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Placed");
    }

    [Test]
    public async Task ACancel_WithANoteOver200_OrATenantId_Returns400()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        var rowVersion = (await OrderHttp.GetAsync(world.TenantAdmin, id)).GetProperty("rowVersion").GetString();

        var longNote = await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", world.Adviser, new { rowVersion, note = new string('n', 201) });
        var tenant = await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", world.Adviser, new { rowVersion, tenantId = world.Tenant.PublicId });

        (await CustodialHttp.ErrorsAsync(longNote))["Note"].ShouldBe(["Note must be 1 to 200 characters."]);
        (await CustodialHttp.ErrorsAsync(tenant)).Keys.ShouldContain("TenantId");
    }

    [Test]
    public async Task ACustomer_SeesTheCancelRequest_WithoutItsNote()
    {
        var world = await OrderWorld.CreateAsync();
        var id = await world.PlaceAsync("Buy", 10m);
        await OrderHttp.RecordAttemptAsync(id);
        (await OrderHttp.CancelAsync(world.Adviser, id, "Internal reason")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var request = (await OrderHttp.GetAsync(world.CustomerToken, id)).GetProperty("cancelRequest");

        request.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        request.GetProperty("by").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        request.GetProperty("at").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Test]
    public async Task AnOrderOutsideTheCallersScope_OfAnotherTenant_OrMissing_Is404()
    {
        var north = await OrderWorld.CreateAsync();
        var south = await OrderWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await north.PlaceAsync("Buy", 10m);
        var southOrder = await south.PlaceAsync("Buy", 10m);
        var bob = await north.OtherAdviserAsync();
        var rowVersion = (await OrderHttp.GetAsync(north.TenantAdmin, id)).GetProperty("rowVersion").GetString();
        var southVersion = (await OrderHttp.GetAsync(south.TenantAdmin, southOrder)).GetProperty("rowVersion").GetString();

        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{id}/cancel", bob, new { rowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{southOrder}/cancel", north.TenantAdmin, new { rowVersion = southVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OrderHttp.Send(HttpMethod.Post, $"/orders/{Guid.NewGuid()}/cancel", north.TenantAdmin, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OrderHttp.GetAsync(south.TenantAdmin, southOrder)).GetProperty("status").GetString().ShouldBe("Placed");
    }
}
