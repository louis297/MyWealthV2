using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Rebalances;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.AdjustmentRequests;

/// <summary>Adjustment review R7–R9, R21: a second TenantAdmin approves a request, and the approval posts the
/// <c>Adjustment</c>.</summary>
[Parallelizable]
public class AdjustmentRequestApproveTests : TestBase
{
    private const string FourEyesMessage =
        "An adjustment must be approved by someone other than the person who requested it.";

    [Test]
    public async Task ASecondTenantAdmin_Approves_Returns204_AndPostsTheAdjustment_WithTheRequestsNumbersAndSource()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        await world.BuyAsync(10m);
        var cashBefore = (await world.CashAsync()).Cash;
        var transactionsBefore = await TransactionCountAsync(world.AccountId);
        var body = world.Body(amount: -0.60m, security: world.Holding(2m, 0.60m));
        body["reference"] = "CN-77";
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, body);
        var before = DateTimeOffset.UtcNow;

        var response = await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve", "Checked against the contract note");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await AdjustmentRequestHttp.GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Approved");
        var decision = item.GetProperty("decision");
        decision.GetProperty("action").GetString().ShouldBe("Approved");
        decision.GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Tia.PublicId);
        decision.GetProperty("by").GetProperty("name").GetString().ShouldBe(world.Tia.Name);
        decision.GetProperty("note").GetString().ShouldBe("Checked against the contract note");
        var approvedAt = decision.GetProperty("at").GetDateTimeOffset();
        approvedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        var transactionId = item.GetProperty("transactionId").GetGuid();

        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore + 1);
        using var transaction = await GetTransactionAsync(world.TenantAdmin, transactionId);
        var root = transaction.RootElement;
        root.GetProperty("type").GetString().ShouldBe("Adjustment");
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("AdjustmentRequest");
        root.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
        root.GetProperty("adjustment").GetProperty("reason").GetString().ShouldBe("EntryError");
        root.GetProperty("bookedAt").GetDateTimeOffset().ShouldBe(approvedAt, TimeSpan.FromMilliseconds(1));
        (await world.CashAsync()).Cash.ShouldBe(cashBefore - 0.60m);
        (await world.HoldingAsync()).ShouldBe((12m, 250.60m));

        var row = await TransactionRowAsync(transactionId);
        row.CreatedBy.ShouldBe(world.Tia.IdentityUserId);
        row.Memo.ShouldBe("Correction after the broker statement");
        row.Reference.ShouldBe("CN-77");
        var stored = await AdjustmentRequestHttp.RowAsync(id);
        stored.TransactionId.ShouldBe(row.Id);
    }

    [Test]
    public async Task TheCustomer_ReadsThePostedAdjustment_AndItsSource_OnTheirOwnAccount_ButNotTheRequest()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var transactionId = (await AdjustmentRequestHttp.GetAsync(world.TenantAdmin, id)).GetProperty("transactionId").GetGuid();

        using var transaction = await GetTransactionAsync(world.CustomerToken, transactionId);

        transaction.RootElement.GetProperty("source").GetProperty("kind").GetString().ShouldBe("AdjustmentRequest");
        transaction.RootElement.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
        (await AdjustmentRequestHttp.Send(HttpMethod.Get, $"{AdjustmentRequestHttp.Path}/{id}", world.CustomerToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TheRequester_Approves_Returns400AtStatus_AndNothingIsWritten()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.TenantAdmin, id, "approve"));

        errors["Status"].ShouldBe([FourEyesMessage]);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task Approve_AfterTheCashWasReserved_Returns400WithTheAvailableCashMessage_StaysRequested_AndIsApprovedAfterADeposit()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: -700m));
        await RequestWithdrawalAsync(world.AccountId, 400m);
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve"));

        errors.Values.SelectMany(messages => messages)
            .ShouldContain("Available cash cannot become negative: 400.00 USD is reserved for withdrawals and orders.");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);

        (await FundAsync(world.TenantAdmin, world.AccountId, 200m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await world.CashAsync()).Cash.ShouldBe(500m);
    }

    [Test]
    public async Task Approve_WhenTheHoldingNoLongerAllowsIt_Returns400AtSecurity_AndStaysRequested()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var buy = await world.BuyAsync(10m);
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(security: world.Holding(-10m, -250m)));
        (await ReverseAsync(world.TenantAdmin, buy)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve"));

        errors["Security"].ShouldBe([Domain.Entities.Transaction.AdjustmentHoldingMessage]);
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task Approve_OnAnAccountClosedSinceTheRequest_Returns400_AndStaysRequested()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        await Orders.OrderHttp.SetAccountStatusAsync(world.AccountId, AccountStatus.Closed);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve"));

        errors.Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task Approve_OfAnApprovedRequest_Returns400AtStatus_AndPostsNothingMore()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var transactionsBefore = await TransactionCountAsync(world.AccountId);

        var errors = await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve"));

        errors["Status"].ShouldBe(["This adjustment request is Approved and cannot be approved."]);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(transactionsBefore);
    }

    [Test]
    public async Task Approve_WithoutARowVersion_Returns400_AndWithAStaleOne_Returns409()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));
        var route = $"{AdjustmentRequestHttp.Path}/{id}/approve";

        (await ErrorsAsync(await AdjustmentRequestHttp.Send(HttpMethod.Post, route, world.SecondTenantAdmin, new { note = "Fine" })))
            .Keys.ShouldContain("RowVersion");
        var stale = await AdjustmentRequestHttp.Send(HttpMethod.Post, route, world.SecondTenantAdmin,
            new { rowVersion = AdjustmentRequestHttp.StaleRowVersion });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
        (await AdjustmentRequestHttp.RowAsync(id)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    [Test]
    public async Task Approve_WithANoteAbove200Characters_Returns400AtNote()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(amount: 5m));

        (await ErrorsAsync(await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve", new string('n', 201))))
            .Keys.ShouldContain("Note");
    }

    [Test]
    public async Task AnAccountWithAnActivePortfolio_IsApproved_KeepsTheAssignment_AndEnqueuesNoRebalance()
    {
        var world = await AdjustmentRequestWorld.CreateAsync();
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", world.TenantAdmin, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = world.InstrumentId, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = createdJson.RootElement.GetProperty("id").GetGuid();
        var assign = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var enqueuedBefore = scheduler.Enqueued.Count;
        var id = await AdjustmentRequestHttp.RequestAsync(world.TenantAdmin, world.Body(security: world.Holding(1m, 25m)));

        (await AdjustmentRequestHttp.ActAsync(world.SecondTenantAdmin, id, "approve")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        scheduler.Enqueued.Count.ShouldBe(enqueuedBefore);
        var assignment = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}/portfolio", world.TenantAdmin);
        var content = await assignment.Content.ReadAsStringAsync();
        assignment.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var assignmentJson = JsonDocument.Parse(content);
        assignmentJson.RootElement.GetProperty("status").GetString().ShouldBe("Active");
        (await world.HoldingAsync()).ShouldBe((1m, 25m));
    }

    [Test]
    public async Task TwoTenants_ApprovingAnotherTenantsRequest_Returns404_AndPostsNothing()
    {
        var north = await AdjustmentRequestWorld.CreateAsync();
        var south = await AdjustmentRequestWorld.CreateAsync("South Advisory", "south-advisory");
        var southId = await AdjustmentRequestHttp.RequestAsync(south.TenantAdmin, south.Body(amount: 5m));

        var response = await AdjustmentRequestHttp.ActAsync(north.SecondTenantAdmin, southId, "approve", readToken: south.SecondTenantAdmin);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AdjustmentRequestHttp.RowAsync(southId)).Status.ShouldBe(AdjustmentRequestStatus.Requested);
    }

    private static async Task<Domain.Entities.Transaction> TransactionRowAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == publicId);
    }
}
