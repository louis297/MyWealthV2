using System.Net;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>Deposit matching R4, R5, R7–R12, R14: reading the statement through the bank feed port.</summary>
[Parallelizable]
public class PullBankStatementsTests : TestBase
{
    [Test]
    public async Task FirstRead_SendsNoCursor_StoresEachLineReceived_AndTheCursor()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Firm", "pull-firm");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var sent = BankFeedHttp.Feed.AddReceipt(
            cma.AccountNumber, 500.00m, "USD", "K7M 3Q9 PXB", payerName: "J SMITH", payerAccount: "12-3456-7890042-00",
            valueDate: new DateOnly(2026, 11, 19));

        var outcome = await BankFeedHttp.PullAsync();

        outcome.AnyFailed.ShouldBeFalse();
        var request = BankFeedHttp.Feed.Requests.Single(entry => entry.AccountNumber == cma.AccountNumber);
        request.Connection.ShouldBe("simulator");
        request.Cursor.ShouldBeNull();
        request.Limit.ShouldBe(100);
        var line = (await BankFeedHttp.LinesAsync(cma.Id)).ShouldHaveSingleItem();
        line.TenantId.ShouldBe(tenant.Id);
        line.Connection.ShouldBe("simulator");
        line.OutsideId.ShouldBe(sent.Id);
        line.ValueDate.ShouldBe(new DateOnly(2026, 11, 19));
        line.Direction.ShouldBe(BankLineDirection.Credit);
        line.Amount.ShouldBe(500.00m);
        line.Currency.ShouldBe("USD");
        line.PayerName.ShouldBe("J SMITH");
        line.PayerAccountMasked.ShouldBe("****4200");
        line.ReferenceText.ShouldBe("K7M 3Q9 PXB");
        line.Status.ShouldBe(BankStatementLineStatus.Received);
        line.ReceivedAt.ShouldBeGreaterThan(before);
        var cursor = (await BankFeedHttp.CursorAsync(cma.Id)).ShouldNotBeNull();
        cursor.Cursor.ShouldBe("1");
        cursor.LastSucceededAt.ShouldNotBeNull().ShouldBeGreaterThan(before);
        cursor.LastFailure.ShouldBeNull();
    }

    [Test]
    public async Task NextRead_SendsTheStoredCursor_AndStoresOnlyTheNewLines()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Next", "pull-next");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 1m, "USD", null);
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 2m, "USD", null);

        await BankFeedHttp.PullAsync();

        BankFeedHttp.Feed.Requests.Where(entry => entry.AccountNumber == cma.AccountNumber)
            .Select(entry => entry.Cursor).ShouldBe([null, "1"]);
        (await BankFeedHttp.LinesAsync(cma.Id)).Select(line => line.Amount).ShouldBe([1m, 2m]);
        (await BankFeedHttp.CursorAsync(cma.Id))!.Cursor.ShouldBe("2");
    }

    [Test]
    public async Task SameOutsideId_TwiceInAPage_AndInALaterPage_IsStoredOnce()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Twice", "pull-twice");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        var line = BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 10m, "USD", null);
        BankFeedHttp.Feed.Add(cma.AccountNumber, line);
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.Add(cma.AccountNumber, line);

        await BankFeedHttp.PullAsync();

        (await BankFeedHttp.LinesAsync(cma.Id)).ShouldHaveSingleItem().OutsideId.ShouldBe(line.Id);
        (await BankFeedHttp.CursorAsync(cma.Id))!.Cursor.ShouldBe("3");
    }

    [Test]
    public async Task ReplacementClientMoneyAccount_StartsWithoutACursor_AndSkipsLinesItsPredecessorStored()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Replace", "pull-replace");
        var old = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        var first = BankFeedHttp.Feed.AddReceipt(old.AccountNumber, 10m, "USD", null);
        await BankFeedHttp.PullAsync();
        var system = await TenantHttp.SignInSystemAdmin();
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, tenant.PublicId, old.PublicId, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var replacementId = await ClientMoneyHttp.CreateAsync(
            system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("USD", accountNumber: old.AccountNumber));
        var second = BankFeedHttp.Feed.AddReceipt(old.AccountNumber, 20m, "USD", null);

        await BankFeedHttp.PullAsync();

        var replacement = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        replacement.PublicId.ShouldBe(replacementId);
        BankFeedHttp.Feed.Requests.Last().Cursor.ShouldBeNull();
        (await BankFeedHttp.LinesAsync(old.Id)).ShouldHaveSingleItem().OutsideId.ShouldBe(first.Id);
        (await BankFeedHttp.LinesAsync(replacement.Id)).ShouldHaveSingleItem().OutsideId.ShouldBe(second.Id);
        (await BankFeedHttp.CursorAsync(replacement.Id))!.Cursor.ShouldBe("2");
    }

    [TestCase("currency")]
    [TestCase("zero")]
    [TestCase("negative")]
    [TestCase("decimals")]
    [TestCase("id")]
    [TestCase("longId")]
    [TestCase("direction")]
    [TestCase("valueDate")]
    public async Task PageWithALineOutsideR10_StoresNothingOfThatPage_KeepsTheCursor_AndSaysInvalidLine(string fault)
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Invalid", "pull-invalid");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 1m, "USD", null);
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 2m, "USD", null);
        var good = new BankFeedLine("bad-" + Guid.NewGuid().ToString("N"), new DateOnly(2026, 11, 20),
            BankLineDirection.Credit, 3m, "USD", null, null, null);
        BankFeedHttp.Feed.Add(cma.AccountNumber, fault switch
        {
            "currency" => good with { Currency = "NZD" },
            "zero" => good with { Amount = 0m },
            "negative" => good with { Amount = -3m },
            "decimals" => good with { Amount = 3.00001m },
            "id" => good with { Id = null },
            "longId" => good with { Id = new string('i', 101) },
            "direction" => good with { Direction = null },
            "valueDate" => good with { ValueDate = null },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        });

        var outcome = await BankFeedHttp.PullAsync();

        outcome.FailedClientMoneyAccounts.ShouldBe([cma.PublicId]);
        (await BankFeedHttp.LinesAsync(cma.Id)).Select(line => line.Amount).ShouldBe([1m]);
        var cursor = (await BankFeedHttp.CursorAsync(cma.Id))!;
        cursor.Cursor.ShouldBe("1");
        cursor.LastFailure.ShouldBe(BankFeedFailure.InvalidLine);
        cursor.LastFailedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task FreeTextIsNotAReasonToRefuse_ItIsCutTo140()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Text", "pull-text");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 1m, "usd", new string('R', 200), payerName: new string('P', 200));

        (await BankFeedHttp.PullAsync()).AnyFailed.ShouldBeFalse();

        var line = (await BankFeedHttp.LinesAsync(cma.Id)).ShouldHaveSingleItem();
        line.ReferenceText!.Length.ShouldBe(140);
        line.PayerName!.Length.ShouldBe(140);
    }

    [TestCase(BankFeedFailure.Unreachable)]
    [TestCase(BankFeedFailure.Timeout)]
    [TestCase(BankFeedFailure.Unauthorized)]
    [TestCase(BankFeedFailure.Unavailable)]
    [TestCase(BankFeedFailure.UnknownAccount)]
    [TestCase(BankFeedFailure.BadResponse)]
    public async Task FailedCall_StoresNothing_KeepsTheCursor_RecordsTheFailure_AndTheNextAccountIsStillRead(
        BankFeedFailure failure)
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Fail", "pull-fail");
        var usd = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id, "USD");
        var nzd = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id, "NZD");
        BankFeedHttp.Feed.AddReceipt(usd.AccountNumber, 1m, "USD", null);
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.AddReceipt(usd.AccountNumber, 2m, "USD", null);
        BankFeedHttp.Feed.AddReceipt(nzd.AccountNumber, 3m, "NZD", null);
        BankFeedHttp.Feed.Fail(usd.AccountNumber, failure);

        var outcome = await BankFeedHttp.PullAsync();

        outcome.FailedClientMoneyAccounts.ShouldBe([usd.PublicId]);
        (await BankFeedHttp.LinesAsync(usd.Id)).Select(line => line.Amount).ShouldBe([1m]);
        var cursor = (await BankFeedHttp.CursorAsync(usd.Id))!;
        cursor.Cursor.ShouldBe("1");
        cursor.LastFailure.ShouldBe(failure);
        cursor.LastFailedAt.ShouldNotBeNull().ShouldBeGreaterThan(cursor.LastSucceededAt!.Value);
        (await BankFeedHttp.LinesAsync(nzd.Id)).Select(line => line.Amount).ShouldBe([3m]);
        (await BankFeedHttp.CursorAsync(nzd.Id))!.LastFailure.ShouldBeNull();
    }

    [Test]
    public async Task MoreThanOnePage_StoresEveryLine_AndKeepsTheCursorOfTheLastPage()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Pages", "pull-pages");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        for (var i = 1; i <= 150; i++)
        {
            BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, i, "USD", null);
        }

        await BankFeedHttp.PullAsync();

        BankFeedHttp.Feed.Requests.Where(entry => entry.AccountNumber == cma.AccountNumber)
            .Select(entry => entry.Cursor).ShouldBe([null, "100"]);
        (await BankFeedHttp.LinesAsync(cma.Id)).Count.ShouldBe(150);
        (await BankFeedHttp.CursorAsync(cma.Id))!.Cursor.ShouldBe("150");
    }

    [Test]
    public async Task OneRun_ReadsAtMostTwentyPagesOfAnAccount_AndTheNextRunContinues()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Cap", "pull-cap");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.PageSize = 1;
        for (var i = 1; i <= 22; i++)
        {
            BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, i, "USD", null);
        }

        await BankFeedHttp.PullAsync();

        BankFeedHttp.Feed.Requests.Count(entry => entry.AccountNumber == cma.AccountNumber).ShouldBe(20);
        (await BankFeedHttp.LinesAsync(cma.Id)).Count.ShouldBe(20);

        await BankFeedHttp.PullAsync();

        (await BankFeedHttp.LinesAsync(cma.Id)).Count.ShouldBe(22);
    }

    [Test]
    public async Task InactiveClientMoneyAccount_IsNotRead_AndEnabledAgainContinuesFromItsCursor()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Inactive", "pull-inactive");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 1m, "USD", null);
        await BankFeedHttp.PullAsync();
        var system = await TenantHttp.SignInSystemAdmin();
        await ClientMoneyHttp.SetActiveAsync(system.AccessToken, tenant.PublicId, cma.PublicId, active: false);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 2m, "USD", null);

        await BankFeedHttp.PullAsync();

        BankFeedHttp.Feed.Requests.Count(entry => entry.AccountNumber == cma.AccountNumber).ShouldBe(1);
        (await BankFeedHttp.LinesAsync(cma.Id)).Count.ShouldBe(1);

        await ClientMoneyHttp.SetActiveAsync(system.AccessToken, tenant.PublicId, cma.PublicId, active: true);
        await BankFeedHttp.PullAsync();

        BankFeedHttp.Feed.Requests.Last(entry => entry.AccountNumber == cma.AccountNumber).Cursor.ShouldBe("1");
        (await BankFeedHttp.LinesAsync(cma.Id)).Select(line => line.Amount).ShouldBe([1m, 2m]);
    }

    [Test]
    public async Task DisabledTenant_IsReadAsUsual()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Disabled", "pull-disabled");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Tenants.SingleAsync(row => row.Id == tenant.Id)).Disable();
            await db.SaveChangesAsync();
        }

        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 1m, "USD", null);

        await BankFeedHttp.PullAsync();

        (await BankFeedHttp.LinesAsync(cma.Id)).ShouldHaveSingleItem();
    }

    [Test]
    public async Task PaymentLines_KeepTheirFields_AndAReturnIsLinkedToTheStoredLineItReturns()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Payments", "pull-payments");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        BankFeedHttp.Feed.Add(cma.AccountNumber, new BankFeedLine(
            "p-L1", today, BankLineDirection.Debit, 300m, "USD", "J Smith", "06-0123-0456789-00", "K7M3Q9PXB", EndToEndId: "6b1f"));
        BankFeedHttp.Feed.Add(cma.AccountNumber, new BankFeedLine(
            "p-L2", today, BankLineDirection.Credit, 300m, "USD", "J Smith", null, null, ReturnOf: "p-L1", ReturnReason: " Account closed "));
        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.Add(cma.AccountNumber, new BankFeedLine(
            "p-L3", today, BankLineDirection.Credit, 300m, "USD", null, null, null, ReturnOf: "p-L1"));
        BankFeedHttp.Feed.Add(cma.AccountNumber, new BankFeedLine(
            "p-L4", today, BankLineDirection.Credit, 300m, "USD", null, null, null, ReturnOf: "never-seen"));

        await BankFeedHttp.PullAsync();

        var lines = (await BankFeedHttp.LinesAsync(cma.Id)).ToDictionary(line => line.OutsideId);
        lines["p-L1"].EndToEndId.ShouldBe("6b1f");
        lines["p-L1"].PayerAccountMasked.ShouldBe("****8900");
        lines["p-L2"].ReturnOfOutsideId.ShouldBe("p-L1");
        lines["p-L2"].ReturnReason.ShouldBe("Account closed");
        lines["p-L2"].RelatedLineId.ShouldBe(lines["p-L1"].Id);
        lines["p-L3"].RelatedLineId.ShouldBe(lines["p-L1"].Id);
        lines["p-L4"].ReturnOfOutsideId.ShouldBe("never-seen");
        lines["p-L4"].RelatedLineId.ShouldBeNull();
        lines.Values.ShouldAllBe(line => line.Status == BankStatementLineStatus.Received);
    }

    [TestCase(101, 0)]
    [TestCase(0, 101)]
    public async Task AnEndToEndIdOrAReturnedLineIdOver100_RefusesThePageAsInvalidLine(int endToEnd, int returnOf)
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Long", "pull-long");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 5m, "USD", null);
        BankFeedHttp.Feed.Add(cma.AccountNumber, new BankFeedLine(
            "long-1", DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, 5m, "USD", null, null, null,
            EndToEndId: endToEnd == 0 ? null : new string('e', endToEnd),
            ReturnOf: returnOf == 0 ? null : new string('r', returnOf)));

        var outcome = await BankFeedHttp.PullAsync();

        outcome.FailedClientMoneyAccounts.ShouldBe([cma.PublicId]);
        (await BankFeedHttp.LinesAsync(cma.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task DebitLine_IsStored_AndStaysReceived()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Debit", "pull-debit");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        BankFeedHttp.Feed.AddReceipt(cma.AccountNumber, 5m, "USD", null, direction: BankLineDirection.Debit);

        await BankFeedHttp.PullAsync();

        var line = (await BankFeedHttp.LinesAsync(cma.Id)).ShouldHaveSingleItem();
        line.Direction.ShouldBe(BankLineDirection.Debit);
        line.Status.ShouldBe(BankStatementLineStatus.Received);
    }

    [Test]
    public async Task ClientMoneyAccountItem_HasNoFeedStatus_BeforeTheFirstRead_ThenTheReadAndTheFailure()
    {
        var tenant = await TestApp.CreateTenantAsync("Pull Status", "pull-status");
        var cma = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id);
        var system = await TenantHttp.SignInSystemAdmin();
        (await ClientMoneyHttp.GetAsync(system.AccessToken, tenant.PublicId, cma.PublicId))
            .GetProperty("bankFeed").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        await BankFeedHttp.PullAsync();
        BankFeedHttp.Feed.Fail(cma.AccountNumber, BankFeedFailure.Unavailable);
        await BankFeedHttp.PullAsync();

        var feed = (await ClientMoneyHttp.GetAsync(system.AccessToken, tenant.PublicId, cma.PublicId)).GetProperty("bankFeed");
        var succeeded = feed.GetProperty("lastSucceededAt").GetDateTimeOffset();
        feed.GetProperty("lastFailedAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(succeeded);
        feed.GetProperty("lastFailure").GetString().ShouldBe("Unavailable");
        var listed = await TenantHttp.Send(HttpMethod.Get, ClientMoneyHttp.Path(tenant.PublicId), system.AccessToken);
        var content = await listed.Content.ReadAsStringAsync();
        content.ShouldContain("\"lastFailure\":\"Unavailable\"");
    }
}
