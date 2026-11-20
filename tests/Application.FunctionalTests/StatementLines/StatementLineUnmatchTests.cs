using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Deposit matching R30, R33, R34: the flow undoes its own deposit.</summary>
[Parallelizable]
public class StatementLineUnmatchTests : TestBase
{
    private const string CashGone =
        "The account no longer holds this cash, so the deposit cannot be undone. Correct it with an adjustment.";

    [Test]
    public async Task Unmatch_OfAMatchByReference_Returns204_PostsTheReversal_AndTheLineIsUnmatched()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);
        var admin = await world.TenantAdminUserAsync();

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new()
        {
            ["note"] = "Wrong customer"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var written = await BankFeedHttp.TransactionsOfLineAsync(line.PublicId);
        written.Select(row => row.Type).ShouldBe([TransactionType.TransferIn, TransactionType.Reversal]);
        var reversal = written[1];
        reversal.OriginalTransactionId.ShouldBe(written[0].Id);
        reversal.CreatedBy.ShouldBe(admin.IdentityUserId);
        reversal.CashLegs.ShouldHaveSingleItem().Amount.ShouldBe(-80m);
        var detail = reversal.BankReceiptDetail.ShouldNotBeNull();
        detail.Reference.ShouldBe(world.DepositReference);
        detail.PayerAccount.ShouldBe("****4200");
        (await world.CashBalanceAsync()).ShouldBe(0m);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe("Unmatched");
        item.GetProperty("unmatchedReason").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("match").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("lastAction").GetProperty("action").GetString().ShouldBe("Unmatched");
        item.GetProperty("lastAction").GetProperty("note").GetString().ShouldBe("Wrong customer");

        using var reversalItem = await CustodialHttp.GetTransactionAsync(world.TenantAdmin.AccessToken, reversal.PublicId);
        reversalItem.RootElement.GetProperty("source").GetProperty("kind").GetString().ShouldBe("BankFeed");
        reversalItem.RootElement.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(line.PublicId);
        reversalItem.RootElement.GetProperty("bankReceipt").GetProperty("reference").GetString().ShouldBe(world.DepositReference);
    }

    [Test]
    public async Task Unmatch_ThenMatchAgain_PostsASecondDepositWithTheSameSourceId()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, "No reference");
        var second = await CustodialHttp.OpenCustodialAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Second");
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = world.AccountId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = "Wrong account" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match", new() { ["accountId"] = second }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).Select(row => row.Type)
            .ShouldBe([TransactionType.TransferIn, TransactionType.Reversal, TransactionType.TransferIn]);
        (await world.CashBalanceAsync()).ShouldBe(0m);
        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin.AccessToken, second)).ShouldBe(80m);
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("match").GetProperty("accountId").GetGuid().ShouldBe(second);
        item.GetProperty("unmatchedReason").GetString().ShouldBe("NoReference");
    }

    [Test]
    public async Task Unmatch_AfterTheCashWasMovedOut_Returns400WithTheR30Message_AndWritesNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Cash(world.AccountId, "TransferOut", -30m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages).ShouldBe([CashGone]);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
        (await world.CashBalanceAsync()).ShouldBe(50m);
    }

    [Test]
    public async Task Unmatch_OnAClosedAccount_Returns400()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);
        (await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Cash(world.AccountId, "CloseOut", -80m)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CustodialHttp.CloseAsync(world.TenantAdmin.AccessToken, world.AccountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task Unmatch_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
    }

    [TestCase(null)]
    [TestCase("  ")]
    public async Task Unmatch_WithoutANote_Returns400AtNote(string? note)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = note });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Note");
    }

    [Test]
    public async Task Unmatch_OfAnUnmatchedLine_Returns400AtStatus()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, null);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new() { ["note"] = "Wrong" });

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe(["This receipt is Unmatched and cannot be unmatched."]);
    }

    [Test]
    public async Task Unmatch_StaleRowVersion_Returns409()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "unmatch", new()
            {
                ["note"] = "Wrong",
                ["rowVersion"] = StatementLineHttp.StaleRowVersion
            }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task TwoTenants_AnotherTenantsLine_Returns404()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var southLine = await south.ReceiveAsync(80m, south.DepositReference);
        var world = await DepositMatchingWorld.CreateAsync();

        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, southLine.PublicId, "unmatch", new()
            {
                ["note"] = "Wrong",
                ["rowVersion"] = await StatementLineHttp.RowVersionAsync(south.TenantAdmin.AccessToken, southLine.PublicId)
            }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await south.CashBalanceAsync()).ShouldBe(80m);
    }

    [Test]
    public async Task HandReverse_OfADeposit_Returns400ByTransactionSourceR13()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await world.ReceiveAsync(80m, world.DepositReference);
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).Single();

        var response = await CustodialHttp.ReverseAsync(world.TenantAdmin.AccessToken, deposit.PublicId);

        (await CustodialHttp.ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldBe(["This transaction was written by the platform and cannot be reversed by hand."]);
        (await world.CashBalanceAsync()).ShouldBe(80m);
    }
}
