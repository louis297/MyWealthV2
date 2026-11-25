using System.Net;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.AdjustmentRequests;

/// <summary>Adjustment review R7, R8, R10–R12: a request is rejected by a second person or cancelled by anyone, and
/// nothing is posted.</summary>
[Parallelizable]
public class AdjustmentRequestRejectCancelTests : TestBase
{
    private const string FourEyesMessage =
        "An adjustment must be approved by someone other than the person who requested it.";

    [Test]
    public async Task ASecondTenantAdmin_Rejects_WithANote_Returns204_Rejected_AndPostsNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        var response = await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "reject", "  Wrong account ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await AdjustmentRequestHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Rejected");
        item.GetProperty("decision").GetProperty("action").GetString().ShouldBe("Rejected");
        item.GetProperty("decision").GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Tia.PublicId);
        item.GetProperty("decision").GetProperty("note").GetString().ShouldBe("Wrong account");
        item.GetProperty("transactionId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
        (await world.CashAsync()).Cash.ShouldBe(1000m);
    }

    [Test]
    public async Task TheRequester_Rejects_Returns400AtStatus()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, id, "reject", "Mine"));

        errors["Status"].ShouldBe([FourEyesMessage]);
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [TestCase(null)]
    [TestCase("   ")]
    public async Task Reject_WithoutANote_Returns400AtNote(string? note)
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));

        (await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "reject", note))).Keys.ShouldContain("Note");
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task TheRequester_Cancels_Returns204_Cancelled_AndPostsNothing()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        (await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await AdjustmentRequestHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("decision").GetProperty("action").GetString().ShouldBe("Cancelled");
        item.GetProperty("decision").GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Tess.PublicId);
        item.GetProperty("decision").GetProperty("note").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
    }

    [Test]
    public async Task ASecondTenantAdmin_Cancels_WithANote()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));

        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "cancel", "No longer needed")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AdjustmentRequestHttp.GetAsync(world.TenantAdmin, id)).GetProperty("decision").GetProperty("note").GetString()
            .ShouldBe("No longer needed");
    }

    [Test]
    public async Task RejectAndCancel_NeedNoAccountCheck_OnAClosedAccountOrADisabledCustomer()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var rejected = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var cancelled = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 6m));
        await Orders.OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);
        await Transactions.LegAccountsFixture.DisableCustomerAsync(world.Customer.Id);

        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, rejected, "reject", "Account closed")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task EveryAction_OnAnEndStatus_Returns400AtStatus()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var approved = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var rejected = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 6m));
        var cancelled = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 7m));
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, approved, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, rejected, "reject", "No")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var (id, status) in new[] { (approved, "Approved"), (rejected, "Rejected"), (cancelled, "Cancelled") })
        {
            foreach (var (action, verb) in new[] { ("approve", "approved"), ("reject", "rejected"), ("cancel", "cancelled") })
            {
                var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, action, "Again"));

                errors["Status"].ShouldBe([$"This adjustment request is {status} and cannot be {verb}."], $"{action} {status}");
            }
        }
    }

    [TestCase("reject")]
    [TestCase("cancel")]
    public async Task AStaleRowVersion_Returns409_AndAMissingOneIs400(string action)
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var route = $"{AdjustmentRequestHttp.Path}/{id}/{action}";

        (await ErrorsAsync(await AdjustmentRequestHttp.Send(HttpMethod.Post, route, world.SecondTenantAdmin, new { note = "No" })))
            .Keys.ShouldContain("RowVersion");
        var stale = await AdjustmentRequestHttp.Send(HttpMethod.Post, route, world.SecondTenantAdmin,
            new { rowVersion = AdjustmentRequestHttp.StaleRowVersion, note = "No" });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsRequest_Returns404_OnEveryAction()
    {
        var north = await AdjustmentRequestWorld.CreateAsync();
        var south = await AdjustmentRequestWorld.CreateAsync("South Advisory", "south-advisory");
        var southId = await AdjustmentRequestHttp.RequestAsync(south.TenantAdmin, south.Body(amount: 5m));

        foreach (var action in new[] { "approve", "reject", "cancel" })
        {
            (await AdjustmentRequestHttp.ActAsync(north.SecondTenantAdmin, southId, action, "Note", readToken: south.TenantAdmin))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await AdjustmentRequestHttp.RowAsync(southId)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }
}
