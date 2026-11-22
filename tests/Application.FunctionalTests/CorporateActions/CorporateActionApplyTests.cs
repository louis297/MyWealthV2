using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Corporate-actions R22–R31: the apply step makes a due hand-entered action effective and posts each
/// entitlement once, on the holding at that moment, with the total cost kept.</summary>
[Parallelizable]
public class CorporateActionApplyTests : TestBase
{
    [Test]
    public async Task AManualSplit_StaysAnnouncedUntilAPriceFromTheExDate_ThenIsEffectiveAndApplied()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var exDate = Today.AddDays(-2);
        var id = await CreateAsync(world.TenantAdmin.AccessToken, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: exDate));
        await AddPriceAsync(holders.InstrumentId, exDate.AddDays(-1), 25m);

        await RunApplyAsync();
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Announced");

        await AddPriceAsync(holders.InstrumentId, exDate, 12.5m);
        var outcome = await RunApplyAsync();

        outcome.Posted.ShouldBe(2);
        var item = await ItemAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Applied");
        item.GetProperty("effectiveBy").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("appliedAt").ValueKind.ShouldBe(JsonValueKind.String);
        var counts = item.GetProperty("entitlements");
        (counts.GetProperty("total").GetInt32(), counts.GetProperty("posted").GetInt32(), counts.GetProperty("pending").GetInt32())
            .ShouldBe((2, 2, 0));
    }

    [Test]
    public async Task TheCustodialHolders_GetASplit_WithTheDetailAndCostZero_AndTheExternalHoldingIsUnchanged()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var nedCost = (await HoldingAsync(token, holders.Ned, holders.InstrumentId)).Cost;
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));

        await RunApplyAsync();

        var ned = await HoldingAsync(token, holders.Ned, holders.InstrumentId);
        (ned.Quantity, ned.Cost).ShouldBe((200m, nedCost));
        (await HoldingAsync(token, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(20m);
        (await HoldingAsync(token, holders.External, holders.InstrumentId)).Quantity.ShouldBe(50m);

        var posting = await PostingOfAsync(token, holders.Ned, id);
        posting.GetProperty("type").GetString().ShouldBe("Split");
        posting.GetProperty("memo").GetString().ShouldBe("Split 2 for 1");
        posting.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        posting.GetProperty("source").GetProperty("kind").GetString().ShouldBe("CorporateAction");
        posting.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(id);
        posting.GetProperty("cash").GetArrayLength().ShouldBe(0);
        var leg = posting.GetProperty("security").EnumerateArray().Single();
        (leg.GetProperty("quantity").GetDecimal(), leg.GetProperty("cost").GetDecimal()).ShouldBe((100m, 0m));

        var entitlements = await EntitlementsAsync(id);
        entitlements.ShouldAllBe(row => row.Status == EntitlementStatus.Posted && row.TransactionId != null);
        entitlements.Select(row => (row.QuantityBefore, row.QuantityAfter)).ShouldBe([(100m, 200m), (10m, 20m)]);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var detail = await db.TransactionCorporateActionDetails.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.TransactionId == entitlements[0].TransactionId);
            (detail.Kind, detail.RatioNew, detail.RatioHeld, detail.ExDate, detail.QuantityBefore, detail.QuantityAfter)
                .ShouldBe((CorporateActionKind.Split, 2, 1, Today, 100m, 200m));
        }
    }

    [Test]
    public async Task ASecondRun_PostsNothing()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        await RunApplyAsync();

        var second = await RunApplyAsync();

        (second.Posted, second.NotApplied, second.Failed).ShouldBe((0, 0, 0));
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(200m);
    }

    [Test]
    public async Task AnAccountThatBuysAfterTheActionIsEffective_GetsNoEntitlement()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Trade(holders.Empty, "Buy", holders.InstrumentId, 2m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await RunApplyAsync();

        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Empty, holders.InstrumentId)).Quantity.ShouldBe(2m);
        (await EntitlementsAsync(id)).Count.ShouldBe(2);
    }

    [Test]
    public async Task AHoldingSoldBetweenEffectiveAndPosting_IsNotAppliedWithNoHolding()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Trade(holders.Olga, "Sell", holders.InstrumentId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var outcome = await RunApplyAsync();

        (outcome.Posted, outcome.NotApplied).ShouldBe((1, 1));
        var olga = (await EntitlementsAsync(id)).Single(row => row.Status == EntitlementStatus.NotApplied);
        olga.Reason.ShouldBe(EntitlementReason.NoHolding);
        olga.TransactionId.ShouldBeNull();
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Applied");
    }

    [Test]
    public async Task AConsolidationThatRoundsATinyHoldingToZero_IsNotAppliedAsTooSmall()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var adjust = await TransactionHttp.Send(HttpMethod.Post, AdjustmentWorld.Path, world.TenantAdmin.AccessToken,
            AdjustmentWorld.Body(holders.Empty, security: AdjustmentWorld.Holding(holders.InstrumentId, 0.00005m, 0.01m)), Guid.NewGuid());
        adjust.StatusCode.ShouldBe(HttpStatusCode.Created, await adjust.Content.ReadAsStringAsync());
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, "Consolidation", 1, 10000, Today));

        await RunApplyAsync();

        var entitlements = await EntitlementsAsync(id);
        var tiny = entitlements.Single(row => row.AccountId == AccountIdOf(holders.Empty));
        (tiny.Status, tiny.Reason).ShouldBe((EntitlementStatus.NotApplied, (EntitlementReason?)EntitlementReason.TooSmall));
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Empty, holders.InstrumentId)).Quantity.ShouldBe(0.00005m);
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(0.01m);
    }

    [Test]
    public async Task OneAccountThatFails_LeavesTheOthersPosted_AndTheNextRunCompletesIt()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        var olga = await AccountIdAsync(holders.Olga);
        await ExecuteAsync($"""
            CREATE TRIGGER [TR_Test_FailCorporateAction] ON [TransactionSecurityLegs] AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE [AccountId] = {olga}) THROW 50001, 'Test failure', 1;
            """);
        try
        {
            var failed = await RunApplyAsync();

            (failed.Posted, failed.Failed).ShouldBe((1, 1));
            failed.HasFailures.ShouldBeTrue();
            (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Effective");
            (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(10m);
            (await EntitlementsAsync(id)).Single(row => row.AccountId == olga).Status.ShouldBe(EntitlementStatus.Pending);
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER [TR_Test_FailCorporateAction];");
        }

        var next = await RunApplyAsync();

        (next.Posted, next.Failed).ShouldBe((1, 0));
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(20m);
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(200m);
        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Applied");
    }

    [Test]
    public async Task AnAssignedAccount_IsPostedInSpiteOfTheTradeLock_KeepsItsAssignment_AndNoRebalanceIsEnqueued()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", token, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = holders.InstrumentId, targetWeight = 100m } }
        });
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Created, await portfolio.Content.ReadAsStringAsync());
        var portfolioId = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        (await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{holders.Ned}/portfolio", token, new { portfolioId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();
        var enqueued = scheduler.Enqueued.Count;
        await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));

        await RunApplyAsync();

        (await HoldingAsync(token, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(200m);
        var assignment = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{holders.Ned}/portfolio", token);
        assignment.StatusCode.ShouldBe(HttpStatusCode.OK, await assignment.Content.ReadAsStringAsync());
        scheduler.Enqueued.Count.ShouldBe(enqueued);
    }

    [Test]
    public async Task ADisabledCustomersAccount_AndADisabledTenant_AreApplied()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        await ExecuteAsync($"""
            UPDATE [Users] SET [Status] = 2 WHERE [PublicId] = '{world.OtherCustomer.PublicId}';
            UPDATE [Tenants] SET [IsActive] = 0 WHERE [Id] = {world.Tenant.Id};
            """);

        var outcome = await RunApplyAsync();

        outcome.Posted.ShouldBe(2);
        await ExecuteAsync($"UPDATE [Tenants] SET [IsActive] = 1 WHERE [Id] = {world.Tenant.Id};");
        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(20m);
    }

    [Test]
    public async Task TheEntitlementPosting_CannotBeReversedByHand_NorTheBuyBeforeIt_AndAHandSplitStaysRefused()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var buy = (await TransactionsOfAsync(token, holders.Ned)).Single(row => row.GetProperty("type").GetString() == "Buy")
            .GetProperty("id").GetGuid();
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 2, exDate: Today));
        await RunApplyAsync();
        var posting = (await PostingOfAsync(token, holders.Ned, id)).GetProperty("id").GetGuid();

        (await CustodialHttp.ReverseAsync(token, posting)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodialHttp.ReverseAsync(token, buy)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var split = CustodialHttp.Trade(holders.Ned, "Split", holders.InstrumentId, 400m);
        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(token, split))).ShouldContainKey("Type");
        (await HoldingAsync(token, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(200m);
    }

    [Test]
    public async Task TheInverseConsolidationWithTheSameExDate_BringsTheQuantityBack()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var cost = (await HoldingAsync(token, holders.Ned, holders.InstrumentId)).Cost;
        await ApplyNowAsync(world, CreateBody(holders.InstrumentId, ratioNew: 4, exDate: Today));
        await RunApplyAsync();
        (await HoldingAsync(token, holders.Ned, holders.InstrumentId)).Quantity.ShouldBe(400m);

        await ApplyNowAsync(world, CreateBody(holders.InstrumentId, "Consolidation", 1, 4, Today));
        await RunApplyAsync();

        var ned = await HoldingAsync(token, holders.Ned, holders.InstrumentId);
        (ned.Quantity, ned.Cost).ShouldBe((100m, cost));
        (await HoldingAsync(token, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(10m);
    }

    [Test]
    public async Task ABonus_IsPostedAsBonus_RoundedDownTo8Decimals()
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var id = await ApplyNowAsync(world, CreateBody(holders.InstrumentId, "Bonus", 1, 3, Today));

        await RunApplyAsync();

        (await HoldingAsync(world.TenantAdmin.AccessToken, holders.Olga, holders.InstrumentId)).Quantity.ShouldBe(13.33333333m);
        var posting = await PostingOfAsync(world.TenantAdmin.AccessToken, holders.Olga, id);
        posting.GetProperty("type").GetString().ShouldBe("Bonus");
        posting.GetProperty("memo").GetString().ShouldBe("Bonus 1 for 3");
    }

    [Test]
    public async Task TwoTenants_ApplyingOneTenantsAction_NeverTouchesTheOtherTenantsAccounts()
    {
        var north = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var northHolders = await HoldersAsync(north);
        var southHolders = await HoldersAsync(south);
        await ApplyNowAsync(north, CreateBody(northHolders.InstrumentId, ratioNew: 2, exDate: Today));

        await RunApplyAsync();

        (await HoldingAsync(north.TenantAdmin.AccessToken, northHolders.Ned, northHolders.InstrumentId)).Quantity.ShouldBe(200m);
        (await HoldingAsync(south.TenantAdmin.AccessToken, southHolders.Ned, southHolders.InstrumentId)).Quantity.ShouldBe(100m);
        (await HoldingAsync(south.TenantAdmin.AccessToken, southHolders.Olga, southHolders.InstrumentId)).Quantity.ShouldBe(10m);
    }

    private static async Task<Guid> ApplyNowAsync(NominatedWorld world, Dictionary<string, object?> body)
    {
        var id = await CreateAsync(world.TenantAdmin.AccessToken, body);
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);
        var response = await Send(HttpMethod.Post, $"{Root}/{id}/apply", world.TenantAdmin.AccessToken, new { rowVersion });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task<(decimal Quantity, decimal Cost)> HoldingAsync(string token, Guid accountId, Guid instrumentId)
    {
        var page = await GetAsync(token, $"/accounts/{accountId}/holdings");
        var item = page.GetProperty("items").EnumerateArray().SingleOrDefault(row => row.GetProperty("instrumentId").GetGuid() == instrumentId);
        return item.ValueKind == JsonValueKind.Undefined
            ? (0m, 0m)
            : (item.GetProperty("quantity").GetDecimal(), item.GetProperty("cost").GetDecimal());
    }

    private static async Task<List<JsonElement>> TransactionsOfAsync(string token, Guid accountId) =>
        (await GetAsync(token, $"/transactions?accountId={accountId}&pageSize=100")).GetProperty("items").EnumerateArray().ToList();

    private static async Task<JsonElement> PostingOfAsync(string token, Guid accountId, Guid actionId) =>
        (await TransactionsOfAsync(token, accountId)).Single(row =>
            row.GetProperty("source").GetProperty("kind").GetString() == "CorporateAction"
            && row.GetProperty("source").GetProperty("id").GetGuid() == actionId);

    private static int AccountIdOf(Guid accountId) => AccountIdAsync(accountId).GetAwaiter().GetResult();

    private static async Task ExecuteAsync(string sql)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
