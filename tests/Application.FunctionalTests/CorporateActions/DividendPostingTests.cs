using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.Rebalances;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionRuns;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R29, R32–R37, R41, R43, R50: the receipt pays a dividend and the apply step posts it.</summary>
[Parallelizable]
public class DividendPostingTests : TestBase
{
    private const decimal Expected = 93.50m;

    [Test]
    public async Task TheReceipt_PaysTheDividend_AndTheApplyRunPostsOneDividendPerAccount()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);
        (await dividend.DividendAsync()).PaidAt.ShouldNotBeNull();

        var outcome = await RunApplyAsync();

        outcome.Posted.ShouldBe(2);
        var item = await ItemAsync(dividend.TenantAdmin, dividend.DividendId);
        item.GetProperty("status").GetString().ShouldBe("Applied");
        item.GetProperty("payment").GetProperty("paidAt").ValueKind.ShouldBe(JsonValueKind.String);

        var ned = await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId);
        ned.GetProperty("type").GetString().ShouldBe("Dividend");
        ned.GetProperty("memo").GetString().ShouldBe("Dividend ACME.SIM 1.00 per share");
        ned.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        ned.GetProperty("source").GetProperty("kind").GetString().ShouldBe("CorporateAction");
        ned.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(dividend.DividendId);
        ned.GetProperty("corporateAction").ValueKind.ShouldBe(JsonValueKind.Null);
        Legs(ned).ShouldBe([("Principal", 100.00m, null), ("Tax", -15.00m, "ForeignWithholding"), ("Tax", -18.00m, "Rwt")], ignoreOrder: true);
        ned.GetProperty("security").GetArrayLength().ShouldBe(0);
        Legs(await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Olga, dividend.DividendId))
            .Sum(leg => leg.Amount).ShouldBe(6.70m);

        var entitlements = await EntitlementsAsync(dividend.DividendId);
        entitlements.ShouldAllBe(row => row.Status == EntitlementStatus.Posted && row.TransactionId != null && row.NetCurrency == "USD");
        entitlements.Select(row => row.NetAmount).ShouldBe([(decimal?)67.00m, 6.70m]);
    }

    [Test]
    public async Task TheTransactionItem_HasTheDividend_AndTheCustomerSeesIt()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);
        await RunApplyAsync();
        var id = (await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId)).GetProperty("id").GetGuid();

        var posting = await GetAsync(dividend.World.CustomerToken.AccessToken, $"/transactions/{id}");

        var detail = posting.GetProperty("dividend");
        detail.GetProperty("corporateActionId").GetGuid().ShouldBe(dividend.DividendId);
        detail.GetProperty("instrument").GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        detail.GetProperty("quantity").GetDecimal().ShouldBe(100m);
        detail.GetProperty("amountPerShare").GetDecimal().ShouldBe(1.00m);
        detail.GetProperty("currency").GetString().ShouldBe("USD");
        detail.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        detail.GetProperty("imputationCredit").GetDecimal().ShouldBe(0m);
        detail.GetProperty("reinvestedQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("reinvestmentPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("exDate").GetString().ShouldBe(Today.ToString("yyyy-MM-dd"));
        detail.GetProperty("paymentDate").GetString().ShouldBe(Today.AddDays(4).ToString("yyyy-MM-dd"));
        var rwt = posting.GetProperty("cash").EnumerateArray().Single(leg => leg.GetProperty("tax").ValueKind != JsonValueKind.Null
            && leg.GetProperty("tax").GetProperty("kind").GetString() == "Rwt").GetProperty("tax");
        (rwt.GetProperty("ratePercent").GetDecimal(), rwt.GetProperty("basisAmount").GetDecimal()).ShouldBe((33m, 100.00m));
    }

    [Test]
    public async Task ASecondRun_PostsNothing_AndNothingIsPostedBeforeTheReceipt()
    {
        var dividend = await DividendWorld.CreateAsync();
        (await RunApplyAsync()).Posted.ShouldBe(0);
        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => row.Status == EntitlementStatus.Pending);

        await dividend.ReceiveAsync(Expected);
        await RunApplyAsync();
        var second = await RunApplyAsync();

        (second.Posted, second.Failed).ShouldBe((0, 0));
    }

    [Test]
    public async Task ANewZealandDividend_TakesRwtFromANewZealandCustomer_AndNrwtFromAnOverseasOne_ByTheProfileAtPosting()
    {
        var dividend = await DividendWorld.CreateAsync(amount: 0.72m, taxSource: DividendTaxSource.NewZealand, credit: 0.28m, rate: 0m);
        await TaxProfileHttp.SetAsync(dividend.TenantAdmin, dividend.World.OtherCustomer.PublicId, "Overseas", true, null);
        await dividend.ReceiveAsync(79.20m);

        await RunApplyAsync();

        Legs(await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId))
            .ShouldBe([("Principal", 72.00m, null), ("Tax", -5.00m, "Rwt")], ignoreOrder: true);
        Legs(await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Olga, dividend.DividendId))
            .ShouldBe([("Principal", 7.20m, null), ("Tax", -2.16m, "Nrwt")], ignoreOrder: true);
        var ned = await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId);
        ned.GetProperty("dividend").GetProperty("imputationCredit").GetDecimal().ShouldBe(28.00m);
    }

    [Test]
    public async Task AForeignDividend_TakesOnlyTheForeignTaxFromAnOverseasCustomer()
    {
        var dividend = await DividendWorld.CreateAsync();
        await TaxProfileHttp.SetAsync(dividend.TenantAdmin, dividend.World.OtherCustomer.PublicId, "Overseas", true, null);
        await dividend.ReceiveAsync(Expected);

        await RunApplyAsync();

        Legs(await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Olga, dividend.DividendId))
            .ShouldBe([("Principal", 10.00m, null), ("Tax", -1.50m, "ForeignWithholding")], ignoreOrder: true);
    }

    [Test]
    public async Task AnAccountInAnotherCurrency_StaysPendingWithoutARate_AndIsConvertedWithAnFxDetailOnceThereIsOne()
    {
        var nzd = Guid.Empty;
        var dividend = await DividendWorld.CreateAsync(setUp: async (world, holders) =>
        {
            nzd = await InterestHttp.OpenCustodialAsync(world.Tenant.Id, world.TenantAdmin.AccessToken, world.Customer.PublicId, "NZD");
            (await CustodialHttp.FundAsync(world.TenantAdmin.AccessToken, nzd, 2000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
            await CustodialHttp.InsertManualTradeAsync(nzd, "Buy", holders.InstrumentId, 20m);
        });
        await dividend.ReceiveAsync(Expected + 17.00m);

        var first = await RunApplyAsync();

        first.Posted.ShouldBe(2);
        (await EntitlementsAsync(dividend.DividendId)).Single(row => row.AccountId == AccountIdOf(nzd)).Status
            .ShouldBe(EntitlementStatus.Pending);
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("status").GetString().ShouldBe("Effective");

        await RebalanceHttp.SetRateAsync("USD", "NZD", 1.7m);
        (await RunApplyAsync()).Posted.ShouldBe(1);

        var posting = await PostingOfAsync(dividend.TenantAdmin, nzd, dividend.DividendId);
        Legs(posting).ShouldBe([("Principal", 34.00m, null), ("Tax", -5.10m, "ForeignWithholding"), ("Tax", -6.12m, "Rwt")],
            ignoreOrder: true);
        posting.GetProperty("cash").EnumerateArray().ShouldAllBe(leg => leg.GetProperty("currency").GetString() == "NZD");
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var postingId = posting.GetProperty("id").GetGuid();
        (await db.Transactions.IgnoreQueryFilters().Include(row => row.FxDetail).SingleAsync(row => row.PublicId == postingId))
            .FxDetail!.Rate.ShouldBe(1.7m);
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("status").GetString().ShouldBe("Applied");
    }

    [Test]
    public async Task OneAccountThatFails_LeavesTheOthersPosted_AndTheNextRunCompletesIt()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);
        var olga = AccountIdOf(dividend.Holders.Olga);
        await ExecuteAsync($"ALTER TABLE [TransactionCashLegs] WITH NOCHECK ADD CONSTRAINT [CK_Test_FailDividend] CHECK ([AccountId] <> {olga});");
        try
        {
            var failed = await RunApplyAsync();
            (failed.Posted, failed.Failed).ShouldBe((1, 1));
        }
        finally
        {
            await ExecuteAsync("ALTER TABLE [TransactionCashLegs] DROP CONSTRAINT [CK_Test_FailDividend];");
        }

        (await RunApplyAsync()).Posted.ShouldBe(1);
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("status").GetString().ShouldBe("Applied");
    }

    [Test]
    public async Task ADisabledCustomer_IsPaid()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);
        await BankFeedHttp.SetCustomerDisabledAsync(dividend.World.OtherCustomer.Id, true);

        (await RunApplyAsync()).Posted.ShouldBe(2);
    }

    [Test]
    public async Task AHandMatchedReceipt_PaysAManualDividend()
    {
        var world = await NominatedBankAccounts.NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateAsync(token, DividendBody(holders.InstrumentId, exDate: Today));
        (await Send(HttpMethod.Post, $"{Root}/{id}/apply", token, new { rowVersion = await RowVersionAsync(token, id) }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id);
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, 24.31m, "USD", "DIV AAPL");
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(credit.Id!);
        (await StatementLineHttp.ActAsync(token, line.PublicId, "match", new() { ["corporateActionId"] = id, ["note"] = "Advice of 15 Dec" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RunApplyAsync()).Posted.ShouldBe(2);
        (await ItemAsync(token, id)).GetProperty("status").GetString().ShouldBe("Applied");
    }

    [Test]
    public async Task ThePosting_CannotBeReversedByHand()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);
        await RunApplyAsync();
        var posting = await PostingOfAsync(dividend.TenantAdmin, dividend.Holders.Ned, dividend.DividendId);

        var reverse = await Send(HttpMethod.Post, $"/transactions/{posting.GetProperty("id").GetGuid()}/reverse", dividend.TenantAdmin,
            new { bookedAt = DateTimeOffset.UtcNow });

        reverse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static List<(string Role, decimal Amount, string? Kind)> Legs(JsonElement posting) =>
        posting.GetProperty("cash").EnumerateArray().Select(leg => (
            leg.GetProperty("role").GetString()!,
            leg.GetProperty("amount").GetDecimal(),
            leg.GetProperty("tax").ValueKind == JsonValueKind.Null ? null : leg.GetProperty("tax").GetProperty("kind").GetString())).ToList();

    private static async Task<JsonElement> PostingOfAsync(string token, Guid accountId, Guid actionId) =>
        (await GetAsync(token, $"/transactions?accountId={accountId}&pageSize=100")).GetProperty("items").EnumerateArray().Single(row =>
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
