using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Withdrawals;

/// <summary>Withdrawals R27–R31: approve, reject and cancel.</summary>
[Parallelizable]
public class WithdrawalActionTests : TestBase
{
    private const string FourEyes = "A withdrawal must be approved by someone other than the person who requested it.";

    [Test]
    public async Task WithoutBearer_Returns401_AndSystemAdmin_Returns403_OnEveryRoute()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var (method, route, body) in WithdrawalHttp.Routes(id))
        {
            (await WithdrawalHttp.Send(method, route, accessToken: null, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
            (await WithdrawalHttp.Send(method, route, system.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }
    }

    [Test]
    public async Task ACustomer_Returns403_OnEveryAction()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);

        foreach (var (method, route, body) in WithdrawalHttp.Routes(id).Skip(2))
        {
            (await WithdrawalHttp.Send(method, route, world.CustomerToken, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
        }
    }

    [Test]
    public async Task Approve_ByATenantAdminWhoDidNotRequestIt_Returns204_AndAPendingPaymentWaits()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(300m);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "approve");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Approved");
        var decision = item.GetProperty("decision");
        decision.GetProperty("action").GetString().ShouldBe("Approved");
        decision.GetProperty("by").GetProperty("name").GetString().ShouldBe("Tess Admin");
        decision.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
        decision.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        var payment = item.GetProperty("payment");
        payment.GetProperty("status").GetString().ShouldBe("Pending");
        payment.GetProperty("attempts").GetInt32().ShouldBe(0);
        payment.GetProperty("lastAttemptAt").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
        payment.GetProperty("sentAt").ValueKind.ShouldBe(JsonValueKind.Null);
        (await world.CashAsync()).ShouldBe((1000m, 300m, 700m));

        var row = await PaymentAsync(id);
        row.InstructionPublicId.ShouldBe(id);
        row.ClientMoneyAccountId.ShouldBe(world.ClientMoney.Id);
        row.Connection.ShouldBe(world.ClientMoney.BankConnection);
        row.Amount.ShouldBe(300m);
        row.Currency.ShouldBe("USD");
        row.Reference.ShouldBe(world.DepositReference);
        row.Status.ShouldBe(OutgoingPaymentStatus.Pending);
    }

    [Test]
    public async Task Approve_WithANote_KeepsIt()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);

        (await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "approve", " Confirmed by phone ")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("decision").GetProperty("note").GetString()
            .ShouldBe("Confirmed by phone");
        (await WithdrawalHttp.GetAsync(world.CustomerToken, id)).GetProperty("decision").GetProperty("note").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [TestCase("approve")]
    [TestCase("reject")]
    public async Task ApproveOrReject_ByTheRequester_Returns400AtStatus(string action)
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await WithdrawalHttp.RequestAsync(world.TenantAdmin, world.AccountId, world.NominatedBankAccountId, 10m);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.TenantAdmin, id, action, "Note")))["Status"].ShouldBe([FourEyes]);
        (await WithdrawalHttp.ActAsync(world.SecondTenantAdmin, id, action, "Note")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [TestCase("approve")]
    [TestCase("reject")]
    public async Task ApproveOrReject_ByAnAdviser_Returns403(string action)
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await WithdrawalHttp.RequestAsync(world.TenantAdmin, world.AccountId, world.NominatedBankAccountId, 10m);

        (await WithdrawalHttp.ActAsync(world.Adviser, id, action, "Note")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Approve_WhenTheNominatedAccountWasRemoved_Returns400AtNominatedBankAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);
        (await NominatedBankAccountHttp.ActAsync(world.Adviser, world.NominatedBankAccountId, "remove", "Closed"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "approve")))["NominatedBankAccountId"]
            .ShouldBe(["The bank account is no longer verified."]);
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Requested");
        (await PaymentOrNullAsync(id)).ShouldBeNull();
    }

    [Test]
    public async Task Approve_WithoutAnActiveClientMoneyAccount_Returns400AtAccountId()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);
        await DisableClientMoneyAsync(world.ClientMoney.Id);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "approve")))["AccountId"]
            .ShouldBe(["There is no active client money account in USD to pay from."]);
        (await PaymentOrNullAsync(id)).ShouldBeNull();
    }

    [Test]
    public async Task Approve_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "approve");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task Reject_WithoutANote_Returns400_AndWithOne_ReleasesTheReservation()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(300m);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "reject"))).ShouldContainKey("Note");
        (await WithdrawalHttp.ActAsync(world.TenantAdmin, id, "reject", " Customer has not confirmed ")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Rejected");
        item.GetProperty("decision").GetProperty("action").GetString().ShouldBe("Rejected");
        item.GetProperty("decision").GetProperty("note").GetString().ShouldBe("Customer has not confirmed");
        item.GetProperty("payment").ValueKind.ShouldBe(JsonValueKind.Null);
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
    }

    [Test]
    public async Task Cancel_FromRequested_ByTheAdviser_ReleasesTheReservation()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(300m);

        (await WithdrawalHttp.ActAsync(world.Adviser, id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await WithdrawalHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("decision").GetProperty("action").GetString().ShouldBe("Cancelled");
        item.GetProperty("decision").GetProperty("by").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
    }

    [Test]
    public async Task Cancel_FromApproved_WithNoAttempt_CancelsThePayment()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);

        (await WithdrawalHttp.ActAsync(world.Adviser, id, "cancel", "Changed mind")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("payment").GetProperty("status").GetString().ShouldBe("Cancelled");
        (await PaymentAsync(id)).Status.ShouldBe(OutgoingPaymentStatus.Cancelled);
        (await world.CashAsync()).Reserved.ShouldBe(0m);
    }

    [Test]
    public async Task Cancel_AfterASendAttempt_Returns400AtStatus()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.ApprovedAsync(300m);
        await RecordAttemptAsync(id);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.Adviser, id, "cancel")))["Status"]
            .ShouldBe(["The bank has been asked to pay this withdrawal, so it can no longer be cancelled."]);
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Approved");
        (await PaymentAsync(id)).Status.ShouldBe(OutgoingPaymentStatus.Pending);
        (await world.CashAsync()).Reserved.ShouldBe(300m);
    }

    [Test]
    public async Task AnActionFromAWrongStatus_Returns400AtStatus()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var approved = await world.ApprovedAsync(10m);
        var cancelled = await world.RequestAsync(10m);
        (await WithdrawalHttp.ActAsync(world.Adviser, cancelled, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.SecondTenantAdmin, approved, "approve")))["Status"]
            .ShouldBe(["This withdrawal is Approved and cannot be approved."]);
        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.SecondTenantAdmin, approved, "reject", "No")))["Status"]
            .ShouldBe(["This withdrawal is Approved and cannot be rejected."]);
        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.ActAsync(world.Adviser, cancelled, "cancel")))["Status"]
            .ShouldBe(["This withdrawal is Cancelled and cannot be cancelled."]);
    }

    [Test]
    public async Task AStaleRowVersion_Returns409_AndAMissingOne_Returns400()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var id = await world.RequestAsync(10m);
        var path = $"{WithdrawalHttp.Path}/{id}/approve";

        var stale = await WithdrawalHttp.Send(HttpMethod.Post, path, world.TenantAdmin, new { rowVersion = WithdrawalHttp.StaleRowVersion });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var json = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString().ShouldBe("stale_row_version");
        }

        (await CustodialHttp.ErrorsAsync(await WithdrawalHttp.Send(HttpMethod.Post, path, world.TenantAdmin, new { note = "x" })))
            .ShouldContainKey("RowVersion");
        (await WithdrawalHttp.GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Requested");
    }

    [Test]
    public async Task AnotherTenantsWithdrawal_Returns404_OnEveryAction()
    {
        var north = await WithdrawalWorld.CreateAsync();
        var south = await WithdrawalWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await south.RequestAsync(10m);

        foreach (var action in new[] { "approve", "reject", "cancel" })
        {
            (await WithdrawalHttp.Send(HttpMethod.Post, $"{WithdrawalHttp.Path}/{id}/{action}", north.TenantAdmin,
                new { note = "No", rowVersion = WithdrawalHttp.StaleRowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await WithdrawalHttp.GetAsync(south.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Requested");
    }

    private static async Task<OutgoingPayment> PaymentAsync(Guid withdrawalId) =>
        await PaymentOrNullAsync(withdrawalId) ?? throw new InvalidOperationException("No outgoing payment.");

    private static async Task<OutgoingPayment?> PaymentOrNullAsync(Guid withdrawalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(row => row.InstructionPublicId == withdrawalId);
    }

    private static async Task RecordAttemptAsync(Guid withdrawalId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.OutgoingPayments.IgnoreQueryFilters().SingleAsync(row => row.InstructionPublicId == withdrawalId);
        payment.RecordAttempt(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task DisableClientMoneyAsync(int clientMoneyAccountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.ClientMoneyAccounts.IgnoreQueryFilters().SingleAsync(row => row.Id == clientMoneyAccountId)).Disable();
        await db.SaveChangesAsync();
    }
}
