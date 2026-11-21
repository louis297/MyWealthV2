using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Fees;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.BankReconciliations;

/// <summary>Bank-reconciliation R6–R15, R17: "Run now" reads the bank side and the Custodial cash of the Tenant and
/// stores the items between them.</summary>
[Parallelizable]
public class BankReconciliationRunTests : TestBase
{
    [Test]
    public async Task AMatchedDepositAndAPaidWithdrawal_Agree_WithNoItems()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.PaidAsync(300m);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await PostAsync(world.TenantAdmin, new { clientMoneyAccountId = world.ClientMoney.PublicId });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.OriginalString.ShouldBe($"{BankReconciliationHttp.Path}/{id}");
        var run = await GetAsync(world.TenantAdmin, id);
        run.GetProperty("id").GetGuid().ShouldBe(id);
        Status(run).ShouldBe("Agreed");
        run.GetProperty("trigger").GetString().ShouldBe("Manual");
        run.GetProperty("ranAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        run.GetProperty("ranBy").GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        run.GetProperty("ranBy").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        Decimal(run, "bankBalance").ShouldBe(700m);
        run.GetProperty("balanceAt").ValueKind.ShouldBe(JsonValueKind.String);
        Decimal(run, "openingBalance").ShouldBe(0m);
        Decimal(run, "statementLinesTotal").ShouldBe(700m);
        Decimal(run, "custodialCash").ShouldBe(700m);
        Decimal(run, "difference").ShouldBe(0m);
        Items(run).ShouldBeEmpty();
        var totals = run.GetProperty("totals");
        foreach (var name in new[] { "breaks", "timing", "known" })
        {
            totals.GetProperty(name).GetProperty("count").GetInt32().ShouldBe(0);
            totals.GetProperty(name).GetProperty("amount").GetDecimal().ShouldBe(0m);
        }

        var account = run.GetProperty("clientMoneyAccount");
        account.GetProperty("id").GetGuid().ShouldBe(world.ClientMoney.PublicId);
        account.GetProperty("currency").GetString().ShouldBe("USD");
        account.GetProperty("bankName").GetString().ShouldBe(world.ClientMoney.BankName);
        account.GetProperty("accountName").GetString().ShouldBe(world.ClientMoney.AccountName);
        account.GetProperty("accountNumber").GetString().ShouldBe(world.ClientMoney.AccountNumber);
        account.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        run.GetProperty("feed").GetProperty("lastSucceededAt").ValueKind.ShouldBe(JsonValueKind.String);
        run.GetProperty("paymentsInFlight").GetProperty("count").GetInt32().ShouldBe(0);
        Decimal(run, "reservedCash").ShouldBe(0m);
    }

    [Test]
    public async Task AnUnmatchedReceipt_IsASuspenseBreak_UntilItIsMatchedByHand()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(500m, "Unknown ref");
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Breaks");
        Items(run).ShouldBe([("Suspense", "Break", 500m, 1)]);
        var item = run.GetProperty("items")[0];
        var snapshot = item.GetProperty("statementLine");
        snapshot.GetProperty("id").GetGuid().ShouldBe(line.PublicId);
        snapshot.GetProperty("direction").GetString().ShouldBe("Credit");
        snapshot.GetProperty("valueDate").GetString().ShouldBe(line.ValueDate.ToString("yyyy-MM-dd"));
        snapshot.GetProperty("status").GetString().ShouldBe("Unmatched");
        snapshot.GetProperty("unmatchedReason").GetString().ShouldBe(line.UnmatchedReason.ToString());
        snapshot.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("clientMoneyAccountId").ValueKind.ShouldBe(JsonValueKind.Null);
        var totals = run.GetProperty("totals").GetProperty("breaks");
        totals.GetProperty("count").GetInt32().ShouldBe(1);
        totals.GetProperty("amount").GetDecimal().ShouldBe(500m);
        Decimal(run, "difference").ShouldBe(500m);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match",
            new() { ["accountId"] = world.AccountId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var again = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(again).ShouldBe("Agreed");
        Items(again).ShouldBeEmpty();
        // R15: the first run is not changed by the second.
        Status(await GetAsync(world.TenantAdmin.AccessToken, run.GetProperty("id").GetGuid())).ShouldBe("Breaks");
    }

    [Test]
    public async Task ALineNotProcessedYet_IsATimingItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 200m, "USD", world.DepositReference);
        await BankFeedHttp.PullAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("NotProcessed", "Timing", 200m, 1)]);
        run.GetProperty("items")[0].GetProperty("statementLine").GetProperty("status").GetString().ShouldBe("Received");
    }

    [Test]
    public async Task AReceiptBeingReturned_IsATimingItem_UntilTheBanksDebitLineReturnsIt()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(75m, null);
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "request-return",
            new() { ["note"] = "Not our customer" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var requested = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        Items(requested).ShouldBe([("ReturnInProgress", "Timing", 75m, 1)]);
        requested.GetProperty("items")[0].GetProperty("statementLine").GetProperty("note").GetString().ShouldBe("Not our customer");

        await BankFeedHttp.SendAsync();
        var sent = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        Items(sent).ShouldBe([("ReturnInProgress", "Timing", 75m, 1)]);
        sent.GetProperty("items")[0].GetProperty("statementLine").GetProperty("status").GetString().ShouldBe("ReturnSent");

        var payment = await ReturnPaymentAsync(line.Id);
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            "return-debit-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, 75m,
            "USD", "J SMITH", "12-3456-7890042-00", null, EndToEndId: payment.ToString(), ReturnOf: line.OutsideId));
        await BankFeedHttp.RunAsync();
        var returned = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(returned).ShouldBe("Agreed");
        Items(returned).ShouldBeEmpty();
        Decimal(returned, "bankBalance").ShouldBe(0m);
    }

    [Test]
    public async Task ARecallThatReversedTheDeposit_IsNoItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        AddRecall(world, receipt.OutsideId, 100m);
        await BankFeedHttp.RunAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBeEmpty();
    }

    [Test]
    public async Task ARecallNotCovered_IsASuspenseBreak_AndAnAdjustmentAndAnIgnoreAgreeTheBooks()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        (await CustodialHttp.WithdrawAsync(world.AccountId, 70m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var recallId = AddRecall(world, receipt.OutsideId, 100m);
        await BankFeedHttp.RunAsync();
        var recall = await BankFeedHttp.LineAsync(recallId);
        recall.UnmatchedReason.ShouldBe(UnmatchedReason.RecallNotCovered);

        var broken = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(broken).ShouldBe("Breaks");
        Items(broken).ShouldBe([("Suspense", "Break", -100m, 1)]);
        broken.GetProperty("items")[0].GetProperty("statementLine").GetProperty("direction").GetString().ShouldBe("Debit");

        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken,
                AdjustmentWorld.Body(world.AccountId, amount: -30m, reason: "ReconciliationBreak"), AdjustmentWorld.Path))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, recall.PublicId, "ignore",
            new() { ["note"] = "Recalled after the cash was paid out" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var fixedRun = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(fixedRun).ShouldBe("Agreed");
        Items(fixedRun).ShouldBe([("Ignored", "Known", -100m, 1), ("Adjustments", "Known", 30m, 1)]);
        Decimal(fixedRun, "difference").ShouldBe(-70m);
    }

    [Test]
    public async Task ABankFeeIgnoredWithANote_IsANegativeKnownItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var fee = "fee-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            fee, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, 15m, "USD", null, null, "Account fee November"));
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(fee);
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "ignore",
            new() { ["note"] = "Bank fee November" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("Ignored", "Known", -15m, 1)]);
        var snapshot = run.GetProperty("items")[0].GetProperty("statementLine");
        snapshot.GetProperty("status").GetString().ShouldBe("Ignored");
        snapshot.GetProperty("unmatchedReason").ValueKind.ShouldBe(JsonValueKind.Null);
        snapshot.GetProperty("note").GetString().ShouldBe("Bank fee November");
        Decimal(run, "bankBalance").ShouldBe(-15m);
    }

    [Test]
    public async Task ACustodialBuyWithAFee_IsAKnownTradesItem()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId, cash: 2000m);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        (await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);

        var run = await RunAndGetAsync(admin.AccessToken, clientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("Trades", "Known", 1260m, 1)]);
        Decimal(run, "bankBalance").ShouldBe(2000m);
        Decimal(run, "custodialCash").ShouldBe(740m);
        run.GetProperty("items")[0].GetProperty("statementLine").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ACustodialFxConversion_IsAnFxConversionsItem_OnTheRunsOfBothCurrencies()
    {
        var world = await FxWorld.CreateAsync();
        var tenantId = world.Legs.Tenant.Id;
        var nzd = await BankFeedHttp.ClientMoneyAccountAsync(tenantId, "NZD");
        var usd = await BankFeedHttp.ClientMoneyAccountAsync(tenantId, "USD");
        await world.FundNzdAsync(100m);
        (await FxConversionTests.PostAsync(world.Legs.TenantAdmin.AccessToken,
            FxConversionTests.SellBody(world.NzdAccountId, world.UsdAccountId, 10m))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await BankFeedHttp.PullAsync();
        var admin = world.Legs.TenantAdmin.AccessToken;

        var nzdRun = await RunAndGetAsync(admin, nzd.PublicId);
        var usdRun = await RunAndGetAsync(admin, usd.PublicId);

        Items(nzdRun).ShouldBe([("FxConversions", "Known", 10m, 1), ("OtherLedgerCash", "Known", -100m, 1)]);
        Items(usdRun).ShouldBe([("FxConversions", "Known", -5.912m, 1)]);
        Status(nzdRun).ShouldBe("Agreed");
        Status(usdRun).ShouldBe("Agreed");
    }

    [Test]
    public async Task ASeededOrHandEnteredTransferIn_IsOtherLedgerCash_AndAnExternalAccountIsNeverCounted()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await LegAccountsFixture.InsertCashTransactionAsync(world.Tenant.Id, TransactionType.TransferIn, (world.AccountId, 40m));
        await CustodialHttp.InsertManualTransferInAsync(world.AccountId, 60m);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Held elsewhere");
        (await CustodialHttp.FundAsync(world.TenantAdmin.AccessToken, external, 900m, external: true)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        await BankFeedHttp.PullAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("OtherLedgerCash", "Known", -100m, 2)]);
        Decimal(run, "custodialCash").ShouldBe(100m);
        Decimal(run, "bankBalance").ShouldBe(0m);
    }

    [Test]
    public async Task AnInternalTransferBetweenCustodialAccounts_IsNoItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(100m, world.DepositReference);
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Second");
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken,
                new Dictionary<string, object?> { ["fromAccountId"] = world.AccountId, ["toAccountId"] = second, ["amount"] = 40m },
                "/transactions/transfer"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBeEmpty();
        Decimal(run, "custodialCash").ShouldBe(100m);
    }

    [Test]
    public async Task ALineTheFeedNeverDelivered_IsAStatementDifferenceBreak()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(100m, world.DepositReference);
        BankFeedHttp.Feed.Omit(world.ClientMoney.AccountNumber, new BankFeedLine(
            "omitted-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, 15m, "USD",
            null, null, "Account fee"));
        await BankFeedHttp.RunAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Breaks");
        Items(run).ShouldBe([("StatementDifference", "Break", -15m, 1)]);
        Decimal(run, "bankBalance").ShouldBe(85m);
        Decimal(run, "statementLinesTotal").ShouldBe(100m);
    }

    [Test]
    public async Task MoneyInTheTrustAccountBeforeTheFirstLine_IsAnOpeningBalanceItem()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        BankFeedHttp.Feed.SetOpeningBalance(world.ClientMoney.AccountNumber, 1000m);
        await world.ReceiveAsync(100m, world.DepositReference);

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("OpeningBalance", "Known", 1000m, 1)]);
        Decimal(run, "openingBalance").ShouldBe(1000m);
    }

    [Test]
    public async Task AReplacementClientMoneyAccount_ShowsTheDepositsOfTheOldAccountAsOtherClientMoneyAccount()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(100m, world.DepositReference);
        var system = await TenantHttp.SignInSystemAdmin();
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, world.Tenant.PublicId, world.ClientMoney.PublicId, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ClientMoneyHttp.CreateAsync(system.AccessToken, world.Tenant.PublicId,
            ClientMoneyHttp.Body("USD", accountNumber: "SIM-NEW-" + Guid.NewGuid().ToString("N")[..8]));
        var replacement = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id);
        BankFeedHttp.Feed.AddReceipt(replacement.AccountNumber, 50m, "USD", world.DepositReference);
        await BankFeedHttp.RunAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, replacement.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBe([("OtherClientMoneyAccount", "Known", -100m, 1)]);
        run.GetProperty("items")[0].GetProperty("clientMoneyAccountId").GetGuid().ShouldBe(world.ClientMoney.PublicId);
        Decimal(run, "custodialCash").ShouldBe(150m);

        // R18: the inactive account can still be run.
        var old = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        Items(old).ShouldBe([("OtherClientMoneyAccount", "Known", -50m, 1)]);
        old.GetProperty("clientMoneyAccount").GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task AnAccountNeverRead_OrAPageWithoutABalance_IsNoBankBalance_WithTheCustodialCashOnly()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await LegAccountsFixture.InsertCashTransactionAsync(world.Tenant.Id, TransactionType.TransferIn, (world.AccountId, 40m));

        var never = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(never).ShouldBe("NoBankBalance");
        Decimal(never, "custodialCash").ShouldBe(40m);
        foreach (var name in new[] { "bankBalance", "openingBalance", "statementLinesTotal", "difference" })
        {
            Decimal(never, name).ShouldBeNull();
        }

        never.GetProperty("balanceAt").ValueKind.ShouldBe(JsonValueKind.Null);
        Items(never).ShouldBeEmpty();
        never.GetProperty("totals").GetProperty("known").GetProperty("count").GetInt32().ShouldBe(0);

        await world.ReceiveAsync(100m, world.DepositReference);
        BankFeedHttp.Feed.SendNoBalance(world.ClientMoney.AccountNumber, true);
        await world.ReceiveAsync(10m, world.DepositReference);
        var noBalance = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(noBalance).ShouldBe("NoBankBalance");
        Decimal(noBalance, "custodialCash").ShouldBe(150m);
    }

    [Test]
    public async Task APaymentAcceptedButNotYetOnTheStatement_IsInformation_NotAnItem()
    {
        var world = await WithdrawalWorld.CreateAsync();
        await world.ApprovedAsync(300m);
        await BankFeedHttp.SendAsync();
        await BankFeedHttp.PullAsync();

        var run = await RunAndGetAsync(world.TenantAdmin, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        Items(run).ShouldBeEmpty();
        var inFlight = run.GetProperty("paymentsInFlight");
        inFlight.GetProperty("count").GetInt32().ShouldBe(1);
        inFlight.GetProperty("amount").GetDecimal().ShouldBe(300m);
        Decimal(run, "reservedCash").ShouldBe(300m);
        Decimal(run, "custodialCash").ShouldBe(1000m);
    }

    [Test]
    public async Task TheRun_ShowsTheFeedStatus_AlsoWhenTheBankIsDown()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(100m, world.DepositReference);
        BankFeedHttp.Feed.Fail(world.ClientMoney.AccountNumber, BankFeedFailure.Unavailable);
        await BankFeedHttp.PullAsync();

        var run = await RunAndGetAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        Status(run).ShouldBe("Agreed");
        var feed = run.GetProperty("feed");
        feed.GetProperty("lastFailure").GetString().ShouldBe("Unavailable");
        feed.GetProperty("lastFailedAt").GetDateTimeOffset()
            .ShouldBeGreaterThanOrEqualTo(feed.GetProperty("lastSucceededAt").GetDateTimeOffset());
    }

    [Test]
    public async Task TheStoredRun_KeepsItsItemsInOrder_WithItsTenantAndThePersonWhoRanIt()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(500m, "Unknown ref");
        BankFeedHttp.Feed.AddReceipt(world.ClientMoney.AccountNumber, 200m, "USD", world.DepositReference);
        await BankFeedHttp.PullAsync();
        await LegAccountsFixture.InsertCashTransactionAsync(world.Tenant.Id, TransactionType.TransferIn, (world.AccountId, 40m));

        var id = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = await db.BankReconciliations.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == id);
        var items = await db.BankReconciliationItems.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.ReconciliationId == run.Id).OrderBy(row => row.Position).ToListAsync();
        items.Select(item => (item.Position, item.Kind)).ShouldBe([
            (1, ReconciliationItemKind.Suspense),
            (2, ReconciliationItemKind.NotProcessed),
            (3, ReconciliationItemKind.OtherLedgerCash)
        ]);
        items.ShouldAllBe(item => item.TenantId == world.Tenant.Id);
        run.TenantId.ShouldBe(world.Tenant.Id);
        run.Trigger.ShouldBe(ReconciliationTrigger.Manual);
        run.RanByUserId.ShouldBe((await world.TenantAdminUserAsync()).Id);
        (run.BreakCount, run.TimingCount, run.KnownCount).ShouldBe((1, 1, 1));
    }

    private static async Task<Guid> ReturnPaymentAsync(int lineId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.ReturnOfLineId == lineId)
            .Select(row => row.InstructionPublicId)
            .SingleAsync();
    }

    /// <summary>The payer's bank takes back <paramref name="receiptId"/> (bank-returns R14).</summary>
    private static string AddRecall(DepositMatchingWorld world, string receiptId, decimal amount)
    {
        var id = "recall-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            id, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, amount, "USD", "J SMITH", "12-3456-7890042-00", null,
            ReturnOf: receiptId, ReturnReason: "Payer's bank recalled the payment"));
        return id;
    }
}
