using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Deposit matching R23–R29, R31: match by hand, ignore, request return and reopen.</summary>
[Parallelizable]
public class StatementLineActionTests : TestBase
{
    private const string ClosedMessage = "Cannot post a transaction while the account is closed. Reopen the account first.";

    [Test]
    public async Task MatchByHand_Returns204_PostsTheDepositAsTheTenantAdmin_AndRecordsTheAction()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(250.00m, "Paid without a reference");
        var admin = await world.TenantAdminUserAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match", new()
        {
            ["accountId"] = world.AccountId,
            ["note"] = "  Confirmed by phone "
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
        deposit.Type.ShouldBe(TransactionType.TransferIn);
        deposit.CreatedBy.ShouldBe(admin.IdentityUserId);
        deposit.Memo.ShouldBe("Bank deposit");
        deposit.BankReceiptDetail!.Reference.ShouldBe("Paid without a reference");
        (await world.CashBalanceAsync()).ShouldBe(250.00m);
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe("Matched");
        item.GetProperty("unmatchedReason").GetString().ShouldBe("NoReference");
        item.GetProperty("match").GetProperty("method").GetString().ShouldBe("Manual");
        item.GetProperty("match").GetProperty("transactionId").GetGuid().ShouldBe(deposit.PublicId);
        var lastAction = item.GetProperty("lastAction");
        lastAction.GetProperty("action").GetString().ShouldBe("Matched");
        lastAction.GetProperty("note").GetString().ShouldBe("Confirmed by phone");
        lastAction.GetProperty("by").GetProperty("id").GetGuid().ShouldBe(admin.PublicId);
        lastAction.GetProperty("by").GetProperty("name").GetString().ShouldBe(admin.Name);
        lastAction.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
    }

    [Test]
    public async Task MatchByHand_WithoutANote_Returns204()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = world.AccountId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId))
            .GetProperty("lastAction").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task MatchByHand_ToAnExternalAccount_Returns400AtAccountId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        var external = await CustodialHttp.OpenExternalAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);

        var response = await StatementLineHttp.ActAsync(
            world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = external });

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["A receipt can be matched only to a Custodial account."]);
        await ShouldBeUnchangedAsync(world, line);
    }

    [Test]
    public async Task MatchByHand_ToAnAccountInAnotherCurrency_Returns400AtAccountId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        var nzd = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Kiwi", "NZD");

        var response = await StatementLineHttp.ActAsync(
            world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = nzd });

        (await CustodialHttp.ErrorsAsync(response))["AccountId"]
            .ShouldBe(["The account's currency is NZD; this receipt is USD."]);
        await ShouldBeUnchangedAsync(world, line);
    }

    [Test]
    public async Task MatchByHand_ToAClosedAccount_Returns400()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        (await CustodialHttp.CloseAsync(world.TenantAdmin.AccessToken, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await StatementLineHttp.ActAsync(
            world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = world.AccountId });

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages).ShouldContain(ClosedMessage);
        await ShouldBeUnchangedAsync(world, line);
    }

    [Test]
    public async Task MatchByHand_ToADisabledCustomersAccount_Returns400DisabledUser()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await StatementLineHttp.ActAsync(
            world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = world.AccountId });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        await ShouldBeUnchangedAsync(world, line);
    }

    [Test]
    public async Task MatchByHand_AnAmountWithTooManyDecimals_Returns400AtAccountId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5.125m, world.DepositReference);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.AmountPrecision);

        var response = await StatementLineHttp.ActAsync(
            world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = world.AccountId });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("AccountId");
        await ShouldBeUnchangedAsync(world, line);
    }

    [Test]
    public async Task MatchByHand_ToAnUnknownAccountOrAnotherTenantsAccount_Returns404()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        foreach (var accountId in new[] { Guid.NewGuid(), south.AccountId })
        {
            (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = accountId }))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await ShouldBeUnchangedAsync(world, line);
        (await south.CashBalanceAsync()).ShouldBe(0m);
    }

    [TestCase("ignore", "Ignored", "Ignored")]
    [TestCase("request-return", "ReturnRequested", "ReturnRequested")]
    public async Task IgnoreAndRequestReturn_WithANote_Return204_AndWriteNothingToTheLedger(
        string action, string status, string lastAction)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, new() { ["note"] = " Bank interest " }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe(status);
        item.GetProperty("lastAction").GetProperty("action").GetString().ShouldBe(lastAction);
        item.GetProperty("lastAction").GetProperty("note").GetString().ShouldBe("Bank interest");
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
    }

    [TestCase("ignore", null)]
    [TestCase("ignore", "   ")]
    [TestCase("request-return", null)]
    [TestCase("request-return", "")]
    [TestCase("ignore", Long201)]
    [TestCase("match", Long201)]
    public async Task NoteOutsideItsRule_Returns400AtNote(string action, string? note)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, new()
        {
            ["note"] = note,
            ["accountId"] = world.AccountId
        });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Note");
        await ShouldBeUnchangedAsync(world, line);
    }

    [TestCase("ignore")]
    [TestCase("request-return")]
    public async Task Reopen_FromIgnoredAndFromReturnRequested_IsUnmatched(string first)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, first, new() { ["note"] = "Note" });

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe("Unmatched");
        item.GetProperty("unmatchedReason").GetString().ShouldBe("NoReference");
        item.GetProperty("lastAction").GetProperty("action").GetString().ShouldBe("Reopened");
        item.GetProperty("lastAction").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Reopen_TakesNoNote()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "ignore", new() { ["note"] = "Note" });

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen", new() { ["note"] = "Why" });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Note");
    }

    [TestCase("match", "matched")]
    [TestCase("ignore", "ignored")]
    [TestCase("request-return", "returned")]
    [TestCase("reopen", "reopened")]
    public async Task Action_FromAMatchedLine_Returns400AtStatus_AndWritesNothing(string action, string word)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, world.DepositReference);
        line.Status.ShouldBe(BankStatementLineStatus.Matched);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, Body(action, world));

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe([$"This receipt is Matched and cannot be {word}."]);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
        (await world.CashBalanceAsync()).ShouldBe(5m);
    }

    [Test]
    public async Task Reopen_FromUnmatched_Returns400AtStatus()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);

        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen")))
            ["Status"].ShouldBe(["This receipt is Unmatched and cannot be reopened."]);
    }

    [TestCase("match")]
    [TestCase("request-return")]
    [TestCase("reopen")]
    [TestCase("unmatch")]
    public async Task ReceiptAction_OnADebitLine_Returns400AtStatus(string action)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 5m, "USD", world.DepositReference, direction: BankLineDirection.Debit);
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(sent.Id!);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, Body(action, world));

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Status");
        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [TestCase("match")]
    [TestCase("ignore")]
    [TestCase("request-return")]
    public async Task StaleRowVersion_Returns409_AndMissingReturns400(string action)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        var stale = Body(action, world);
        stale["rowVersion"] = StatementLineHttp.StaleRowVersion;
        var missing = Body(action, world);
        missing["rowVersion"] = null;

        var conflict = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, stale);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var json = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString().ShouldBe("stale_row_version");
        }

        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, missing)))
            .ShouldContainKey("RowVersion");
        await ShouldBeUnchangedAsync(world, line);
    }

    [TestCase("match")]
    [TestCase("ignore")]
    [TestCase("reopen")]
    public async Task TenantIdInTheBody_Returns400(string action)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(5m, null);
        var body = Body(action, world);
        body["tenantId"] = world.Tenant.PublicId;

        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, action, body)))
            .ShouldContainKey("TenantId");
    }

    [TestCase("match")]
    [TestCase("ignore")]
    [TestCase("request-return")]
    [TestCase("reopen")]
    public async Task TwoTenants_AnotherTenantsLine_Returns404(string action)
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var southLine = await south.ReceiveAsync(5m, null);
        var world = await DepositMatchingWorld.CreateAsync();
        var body = Body(action, world);
        body["rowVersion"] = await StatementLineHttp.RowVersionAsync(south.TenantAdmin.AccessToken, southLine.PublicId);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, southLine.PublicId, action, body))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await BankFeedHttp.LineAsync(southLine.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    private const string Long201 =
        "nnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnnn";

    private static Dictionary<string, object?> Body(string action, DepositMatchingWorld world) => action switch
    {
        "match" => new() { ["accountId"] = world.AccountId },
        "reopen" => new(),
        _ => new() { ["note"] = "Note" }
    };

    private static async Task ShouldBeUnchangedAsync(DepositMatchingWorld world, BankStatementLine line)
    {
        var now = await BankFeedHttp.LineAsync(line.OutsideId);
        now.Status.ShouldBe(line.Status);
        now.LastAction.ShouldBe(line.LastAction);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }
}
