using System.Net;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R26, R27: a TenantAdmin matches a receipt to a dividend, and a matched receipt stays.</summary>
[Parallelizable]
public class DividendHandMatchTests : TestBase
{
    private const string Note = "Dividend AAPL, custodian advice of 15 Dec";

    /// <summary>A Manual dividend of AAPL (USD) that went ex on Ned 100 and Olga 10, and a USD receipt in suspense.</summary>
    private static async Task<(NominatedWorld World, Guid DividendId, BankStatementLine Line)> ManualAsync(decimal received = 2.86m)
    {
        var world = await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world);
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateAsync(token, DividendBody(holders.InstrumentId, exDate: Today));
        (await Send(HttpMethod.Post, $"{Root}/{id}/apply", token, new { rowVersion = await RowVersionAsync(token, id) }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var line = await ReceiveAsync(world, received);
        return (world, id, line);
    }

    private static async Task<BankStatementLine> ReceiveAsync(NominatedWorld world, decimal amount, string currency = "USD")
    {
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id, currency);
        var credit = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, amount, currency, "DIV AAPL", "Simulator Custody");
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(credit.Id!);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        return line;
    }

    private static Task<HttpResponseMessage> MatchAsync(string token, Guid lineId, Guid dividendId, string? note = Note) =>
        StatementLineHttp.ActAsync(token, lineId, "match", new() { ["corporateActionId"] = dividendId, ["note"] = note });

    [Test]
    public async Task TheReceiptOfAManualDividend_IsMatchedByHand_WithItsNote()
    {
        // 100 × 0.26 − 15 % = 22.10; 10 × 0.26 − 15 % = 2.21.
        var (world, id, line) = await ManualAsync(24.31m);
        var token = world.TenantAdmin.AccessToken;

        (await MatchAsync(token, line.PublicId, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var matched = await BankFeedHttp.LineAsync(line.OutsideId);
        (matched.Status, matched.MatchMethod, matched.MatchedAccountId, matched.LastActionNote).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Dividend, (int?)null, Note));
        var payment = (await ItemAsync(token, id)).GetProperty("payment");
        payment.GetProperty("expectedAmount").GetDecimal().ShouldBe(24.31m);
        payment.GetProperty("receivedAmount").GetDecimal().ShouldBe(24.31m);
        payment.GetProperty("statementLineId").GetGuid().ShouldBe(line.PublicId);
    }

    [Test]
    public async Task AReceiptOfAnotherAmount_IsAccepted_AndTheDifferenceStaysVisible()
    {
        var (world, id, line) = await ManualAsync(20.00m);

        (await MatchAsync(world.TenantAdmin.AccessToken, line.PublicId, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ItemAsync(world.TenantAdmin.AccessToken, id)).GetProperty("payment").GetProperty("difference")
            .GetDecimal().ShouldBe(-4.31m);
    }

    [Test]
    public async Task ADividendMismatchLine_IsAcceptedByHand()
    {
        var dividend = await DividendWorld.CreateAsync();
        var line = await dividend.ReceiveAsync(90m);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.DividendMismatch);

        (await MatchAsync(dividend.TenantAdmin, line.PublicId, dividend.DividendId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await dividend.DividendAsync()).ReceivedAmount.ShouldBe(90m);
    }

    [Test]
    public async Task NoTarget_OrTwoTargets_Returns400AtAccountId_AndANoteIsRequired()
    {
        var (world, id, line) = await ManualAsync();
        var token = world.TenantAdmin.AccessToken;

        var none = await StatementLineHttp.ActAsync(token, line.PublicId, "match", new() { ["note"] = Note });
        (await CustodialHttp.ErrorsAsync(none))["AccountId"].ShouldBe(["Give an account, a migration batch or a dividend."]);
        var two = await StatementLineHttp.ActAsync(token, line.PublicId, "match",
            new() { ["corporateActionId"] = id, ["accountId"] = Guid.NewGuid(), ["note"] = Note });
        (await CustodialHttp.ErrorsAsync(two)).ShouldContainKey("AccountId");
        (await CustodialHttp.ErrorsAsync(await MatchAsync(token, line.PublicId, id, note: null))).ShouldContainKey("Note");
        (await CustodialHttp.ErrorsAsync(await MatchAsync(token, line.PublicId, id, note: "   "))).ShouldContainKey("Note");
    }

    [Test]
    public async Task ASplit_ADividendWithAReceipt_OrOneThatHasNotGoneEx_Returns400AtCorporateActionId()
    {
        var (world, id, line) = await ManualAsync();
        var token = world.TenantAdmin.AccessToken;
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(token, "MSFT");
        var split = await CreateAsync(token, CreateBody(instrumentId));
        var announced = await CreateAsync(token, DividendBody(instrumentId, exDate: Today.AddDays(5)));
        const string Message = "Only a dividend that waits for its payment can take a receipt.";

        (await CustodialHttp.ErrorsAsync(await MatchAsync(token, line.PublicId, split)))["CorporateActionId"].ShouldBe([Message]);
        (await CustodialHttp.ErrorsAsync(await MatchAsync(token, line.PublicId, announced)))["CorporateActionId"].ShouldBe([Message]);

        (await MatchAsync(token, line.PublicId, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = await ReceiveAsync(world, 1m);
        (await CustodialHttp.ErrorsAsync(await MatchAsync(token, second.PublicId, id)))["CorporateActionId"].ShouldBe([Message]);
    }

    [Test]
    public async Task AReceiptInAnotherCurrency_Returns400AtCorporateActionId()
    {
        var (world, id, _) = await ManualAsync();
        var nzd = await ReceiveAsync(world, 2.86m, "NZD");

        (await CustodialHttp.ErrorsAsync(await MatchAsync(world.TenantAdmin.AccessToken, nzd.PublicId, id)))["CorporateActionId"]
            .ShouldBe(["This dividend is paid in USD and this receipt is in NZD."]);
    }

    [Test]
    public async Task AMatchedReceipt_CannotBeUnmatched()
    {
        var (world, id, line) = await ManualAsync();
        var token = world.TenantAdmin.AccessToken;
        (await MatchAsync(token, line.PublicId, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var unmatch = await StatementLineHttp.ActAsync(token, line.PublicId, "unmatch", new() { ["note"] = "Wrong dividend" });

        (await CustodialHttp.ErrorsAsync(unmatch))["Status"].ShouldBe(["A receipt matched to a dividend cannot be taken out of it."]);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task AnAdviser_Gets403_AndAnotherTenantsDividend_Returns404()
    {
        var (world, id, line) = await ManualAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var southInstrument = await CustodialHttp.CreateInstrumentAsync(south.TenantAdmin.AccessToken, "AAPL");
        var southId = await CreateAsync(south.TenantAdmin.AccessToken, DividendBody(southInstrument));

        (await Send(HttpMethod.Post, $"/statement-lines/{line.PublicId}/match", world.AdviserToken.AccessToken,
                new { corporateActionId = id, rowVersion = "AAAAAAAAAAE=", note = Note }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MatchAsync(world.TenantAdmin.AccessToken, line.PublicId, southId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
