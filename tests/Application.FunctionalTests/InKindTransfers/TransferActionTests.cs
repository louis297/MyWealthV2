using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Orders;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R30–R33: approve and reject by a second person, cancel and set aside.</summary>
[Parallelizable]
public class TransferActionTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_OnEveryAction()
    {
        foreach (var action in new[] { "approve", "reject", "cancel", "set-aside" })
        {
            var response = await TransferHttp.Send(HttpMethod.Post, $"{TransferHttp.Path}/{Guid.NewGuid()}/{action}", null,
                new { rowVersion = TransferHttp.StaleRowVersion, note = "Note" });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, action);
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task AnAdviser_Returns403_OnApproveRejectAndSetAside_AndACustomerOnCancel()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();

        foreach (var action in new[] { "approve", "reject", "set-aside" })
        {
            (await TransferHttp.ActAsync(world.Adviser, id, action, "Note")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, action);
        }

        (await TransferHttp.ActAsync(world.CustomerToken, id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Requested);
    }

    [Test]
    public async Task Approve_ByAnotherTenantAdmin_IsApproved_WithTheDecision_AndTheSafekeepingAccount()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();

        var response = await TransferHttp.ActAsync(world.TenantAdmin, id, "approve", " Harbour statement sighted ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await TransferHttp.GetAsync(world.Adviser, id);
        item.GetProperty("status").GetString().ShouldBe("Approved");
        item.GetProperty("decision").GetProperty("action").GetString().ShouldBe("Approve");
        item.GetProperty("decision").GetProperty("note").GetString().ShouldBe("Harbour statement sighted");
        (await TransferHttp.TransferAsync(id)).SafekeepingAccountId.ShouldBe(world.Safekeeping.Id);
        (await TransferHttp.GetAsync(world.CustomerToken, id)).GetProperty("decision").GetProperty("note").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [TestCase("approve")]
    [TestCase("reject")]
    public async Task ApproveOrReject_ByTheRequester_Returns400AtStatus(string action)
    {
        var world = await TransferWorld.CreateAsync();
        var id = await TransferHttp.RequestAsync(world.TenantAdmin, world.Body());

        var response = await TransferHttp.ActAsync(world.TenantAdmin, id, action, "Note");

        (await CustodialHttp.ErrorsAsync(response))["Status"]
            .ShouldBe(["A transfer of securities must be approved by someone other than the person who requested it."]);
        (await TransferHttp.ActAsync(world.SecondTenantAdmin, id, action, "Note")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Approve_Twice_Returns400WithTheStatus()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync();

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.SecondTenantAdmin, id, "approve")))["Status"]
            .ShouldBe(["This transfer is Approved and cannot be approved."]);
    }

    [Test]
    public async Task Approve_WhenTheAccountClosedSinceTheRequest_Returns400()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        await OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "approve")))
            .Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
    }

    [Test]
    public async Task Approve_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await TransferHttp.ActAsync(world.TenantAdmin, id, "approve");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
    }

    [Test]
    public async Task Approve_WhenTheSafekeepingAccountWasDisabled_Returns400AtInstrumentId()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.SafekeepingAccounts.IgnoreQueryFilters().SingleAsync(row => row.Id == world.Safekeeping.Id)).Disable();
            await db.SaveChangesAsync();
        }

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "approve")))["InstrumentId"]
            .ShouldBe(["The firm has no safekeeping account for securities quoted in USD, so ACME cannot be transferred."]);
    }

    [Test]
    public async Task ApproveIn_WhenTheInstrumentWasDisabled_Returns400AtInstrumentId()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        await TransferHttp.DisableInstrumentAsync(world.TenantAdmin, world.InstrumentId);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "approve")))["InstrumentId"]
            .ShouldBe(["Instrument is disabled."]);
    }

    [Test]
    public async Task Reject_NeedsANote_AndReleasesTheReservation()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync("Out", 40m);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "reject"))).ShouldContainKey("Note");
        (await TransferHttp.ActAsync(world.TenantAdmin, id, "reject", "Wrong holder")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TransferHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Rejected");
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Cancel_FromRequested_AndFromApproved_ReleasesTheReservation()
    {
        var world = await TransferWorld.CreateAsync();
        var requested = await world.RequestAsync("Out", 40m);
        var approved = await world.ApprovedAsync("Out", 10m);

        (await TransferHttp.ActAsync(world.Adviser, requested, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TransferHttp.ActAsync(world.Adviser, approved, "cancel", "Customer changed their mind")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await TransferHttp.GetAsync(world.TenantAdmin, requested)).GetProperty("status").GetString().ShouldBe("Cancelled");
        (await TransferHttp.GetAsync(world.TenantAdmin, approved)).GetProperty("decision").GetProperty("action").GetString()
            .ShouldBe("Cancel");
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Cancel_AfterASendAttempt_Returns400AtStatus()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync();
        await TransferHttp.RecordAttemptAsync(id);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.Adviser, id, "cancel")))["Status"]
            .ShouldBe(["The custodian has been asked to make this transfer, so it can no longer be cancelled."]);
    }

    [Test]
    public async Task SetAside_NeedsAnAttempt_AndANote_ThenEndsTheTransfer_AndReleasesTheReservation()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync("Out", 40m);

        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "set-aside", "Note")))["Status"]
            .ShouldBe(["This transfer is Approved and cannot be set aside."]);
        await TransferHttp.RecordAttemptAsync(id);
        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.TenantAdmin, id, "set-aside"))).ShouldContainKey("Note");

        (await TransferHttp.ActAsync(world.TenantAdmin, id, "set-aside", "The custodian never executed it")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("SetAside");
        item.GetProperty("setAside").GetProperty("note").GetString().ShouldBe("The custodian never executed it");
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
        (await TransferHttp.GetAsync(world.CustomerToken, id)).GetProperty("setAside").GetProperty("note").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task MissingRowVersion_Returns400_AndAStaleOne_Returns409()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.RequestAsync();
        var route = $"{TransferHttp.Path}/{id}/approve";

        (await CustodialHttp.ErrorsAsync(await TransferHttp.Send(HttpMethod.Post, route, world.TenantAdmin, new { })))
            .ShouldContainKey("RowVersion");
        var stale = await TransferHttp.Send(HttpMethod.Post, route, world.TenantAdmin, new { rowVersion = TransferHttp.StaleRowVersion });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsTransfer_Returns404_OnEveryAction()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await south.RequestAsync();
        var rowVersion = (await TransferHttp.GetAsync(south.TenantAdmin, id)).GetProperty("rowVersion").GetString();

        foreach (var action in new[] { "approve", "reject", "cancel", "set-aside" })
        {
            (await TransferHttp.Send(HttpMethod.Post, $"{TransferHttp.Path}/{id}/{action}", north.TenantAdmin,
                    new { rowVersion, note = "Note" }))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await TransferHttp.TransferAsync(id)).Status.ShouldBe(InKindTransferStatus.Requested);
    }
}
