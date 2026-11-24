using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.InKindTransfers;

/// <summary>In-kind transfers R34, R35: the send step, with the fake custodian.</summary>
[Parallelizable]
public class SendInKindTransfersTests : TestBase
{
    [Test]
    public async Task AnApprovedTransfer_IsSentOnce_WithItsPublicId_AndTheSafekeepingAccount_AndAcceptedIsSent()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync("Out", 40m);

        var outcome = await TransferHttp.SendAsync();
        await TransferHttp.SendAsync();

        outcome.ShouldBe(new CustodianSendOutcome(1, 0));
        var request = TransferHttp.Custodian.Requests.ShouldHaveSingleItem();
        request.InstructionId.ShouldBe(id);
        request.Connection.ShouldBe("simulator");
        request.AccountNumber.ShouldBe(world.Safekeeping.AccountNumber);
        (request.Direction, request.Symbol, request.Currency, request.Quantity)
            .ShouldBe((TransferDirection.Out, "ACME", "USD", 40m));
        request.Counterparty.ShouldBe(new CustodianCounterparty("Harbour Brokers", "HB-0044172", "J Smith"));
        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Sent");
        item.GetProperty("send").GetProperty("attempts").GetInt32().ShouldBe(1);
        item.GetProperty("send").GetProperty("custodianTransferId").GetString().ShouldBe("fake-T1");
        item.GetProperty("send").GetProperty("acceptedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("reservedQuantity").GetDecimal().ShouldBe(40m);
    }

    [Test]
    public async Task Rejected_IsFailed_WithTheReason_AndReleasesTheReservation()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync("Out", 40m);
        TransferHttp.Custodian.Answer(CustodianTransferResult.Rejected(TransferRejectReason.InsufficientPosition));

        await TransferHttp.SendAsync();

        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Failed");
        item.GetProperty("failureReason").GetString().ShouldBe("InsufficientPosition");
        (await TransferHttp.HoldingAsync(world.TenantAdmin, world.AccountId, world.InstrumentId))!.Value
            .GetProperty("reservedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task AFailedCall_LeavesItApproved_WithTheFailure_AndTheNextRunSendsItAgainWithTheSameId()
    {
        var world = await TransferWorld.CreateAsync();
        var id = await world.ApprovedAsync();
        TransferHttp.Custodian.Answer(CustodianTransferResult.Failed(CustodianFailure.Timeout));

        (await TransferHttp.SendAsync()).ShouldBe(new CustodianSendOutcome(1, 1));
        var item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Approved");
        item.GetProperty("send").GetProperty("lastFailure").GetString().ShouldBe("Timeout");
        (await CustodialHttp.ErrorsAsync(await TransferHttp.ActAsync(world.Adviser, id, "cancel")))["Status"]
            .ShouldBe(["The custodian has been asked to make this transfer, so it can no longer be cancelled."]);

        await TransferHttp.SendAsync();

        TransferHttp.Custodian.Requests.Select(request => request.InstructionId).ShouldBe([id, id]);
        item = await TransferHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Sent");
        item.GetProperty("send").GetProperty("attempts").GetInt32().ShouldBe(2);
        item.GetProperty("send").GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ARequestedTransfer_AndOneOnAnInactiveSafekeepingAccount_AreNotSent()
    {
        var world = await TransferWorld.CreateAsync();
        await world.RequestAsync();
        var approved = await world.ApprovedAsync();
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.SafekeepingAccounts.IgnoreQueryFilters().SingleAsync(row => row.Id == world.Safekeeping.Id)).Disable();
            await db.SaveChangesAsync();
        }

        await TransferHttp.SendAsync();

        TransferHttp.Custodian.Requests.ShouldBeEmpty();
        (await TransferHttp.TransferAsync(approved)).Status.ShouldBe(InKindTransferStatus.Approved);
    }

    [Test]
    public async Task ADisabledTenantsTransfer_GetsNoFirstAttempt_ButOneAlreadyAttemptedIsSentAgain()
    {
        var world = await TransferWorld.CreateAsync();
        var fresh = await world.ApprovedAsync();
        var attempted = await world.ApprovedAsync();
        await TransferHttp.RecordAttemptAsync(attempted);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Tenants.SingleAsync(row => row.Id == world.Tenant.Id)).Disable();
            await db.SaveChangesAsync();
        }

        await TransferHttp.SendAsync();

        TransferHttp.Custodian.Requests.Select(request => request.InstructionId).ShouldBe([attempted]);
        (await TransferHttp.TransferAsync(fresh)).Attempts.ShouldBe(0);
    }

    [Test]
    public async Task AnotherConnection_SendsNothing()
    {
        var world = await TransferWorld.CreateAsync();
        await world.ApprovedAsync();

        (await TransferHttp.SendAsync("other")).ShouldBe(new CustodianSendOutcome(0, 0));

        TransferHttp.Custodian.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task AnswersOfTwoTenants_AreKeptApart()
    {
        var north = await TransferWorld.CreateAsync();
        var south = await TransferWorld.CreateAsync("South Advisory", "south-advisory");
        var northId = await north.ApprovedAsync();
        var southId = await south.ApprovedAsync();

        await TransferHttp.SendAsync();

        TransferHttp.Custodian.Requests.Select(request => (request.InstructionId, request.AccountNumber))
            .ShouldBe([(northId, north.Safekeeping.AccountNumber), (southId, south.Safekeeping.AccountNumber)]);
        (await TransferHttp.Send(HttpMethod.Get, $"{TransferHttp.Path}/{southId}", north.TenantAdmin)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }
}
