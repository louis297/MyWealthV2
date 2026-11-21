using System.Net;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Bank-returns R2, R4, R9: debit lines the platform did not cause go to suspense, and a TenantAdmin ignores
/// or reopens them.</summary>
[Parallelizable]
public class StatementLineDebitTests : TestBase
{
    [Test]
    public async Task ADebitWithNoEndToEndIdAndNoReturnedLine_IsAnUnknownPayment_AndPostsNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await world.ReceiveAsync(100m, world.DepositReference);
        var sent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 12.50m, "USD", "Bank fee", direction: BankLineDirection.Debit);

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(sent.Id!);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        line.MatchedAccountId.ShouldBeNull();
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        (await world.CashBalanceAsync()).ShouldBe(100m);
    }

    [TestCase("not-a-guid")]
    [TestCase("6b1f0a52-0000-4000-8000-000000000001")]
    public async Task ADebitWithAnEndToEndIdThatNamesNothing_IsAnUnknownPayment(string endToEndId)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var id = "debit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            id, new DateOnly(2026, 11, 21), BankLineDirection.Debit, 20m, "USD", "J SMITH", "12-3456-7890042-00", null,
            EndToEndId: endToEndId));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(id);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
    }

    [Test]
    public async Task ADebitNamingAWithdrawalWhosePaymentWasRejected_IsAnUnknownPayment()
    {
        var world = await WithdrawalWorld.CreateAsync();
        var withdrawal = await world.ApprovedAsync(100m);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Rejected(PaymentRejectReason.Refused));
        await BankFeedHttp.SendAsync();
        var outsideId = world.AddPayoutLine(withdrawal, 100m);

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(outsideId);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        (await world.CashAsync()).ShouldBe((1000m, 0m, 1000m));
    }

    [Test]
    public async Task ADebitLineStoredBeforeThisSlice_IsProcessedByTheFirstRun()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 3m, "USD", null, direction: BankLineDirection.Debit);
        await BankFeedHttp.PullAsync();
        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Received);

        await BankFeedHttp.ProcessAsync();

        (await BankFeedHttp.LineAsync(sent.Id!)).UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
    }

    [Test]
    public async Task Ignore_OfAnUnknownDebit_Returns204_ThenReopen_Returns204()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await UnknownDebitAsync(world);
        var token = world.TenantAdmin.AccessToken;

        var ignore = await StatementLineHttp.ActAsync(token, line, "ignore", new() { ["note"] = " Monthly bank fee " });

        ignore.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ignore.Content.ReadAsStringAsync());
        var ignored = await StatementLineHttp.GetAsync(token, line);
        ignored.GetProperty("status").GetString().ShouldBe("Ignored");
        ignored.GetProperty("unmatchedReason").GetString().ShouldBe("UnknownPayment");
        ignored.GetProperty("lastAction").GetProperty("note").GetString().ShouldBe("Monthly bank fee");

        var reopen = await StatementLineHttp.ActAsync(token, line, "reopen");

        reopen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reopen.Content.ReadAsStringAsync());
        (await StatementLineHttp.GetAsync(token, line)).GetProperty("status").GetString().ShouldBe("Unmatched");
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [TestCase("match")]
    [TestCase("request-return")]
    [TestCase("unmatch")]
    [TestCase("reopen")]
    public async Task OtherActions_OnAnUnknownDebit_Return400AtStatus_AndChangeNothing(string action)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var line = await UnknownDebitAsync(world);
        Dictionary<string, object?> body = action switch
        {
            "match" => new() { ["accountId"] = world.AccountId },
            "reopen" => new(),
            _ => new() { ["note"] = "Note" }
        };

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line, action, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Status");
        var after = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line);
        after.GetProperty("status").GetString().ShouldBe("Unmatched");
        (await BankFeedHttp.TransactionsOfLineAsync(line)).ShouldBeEmpty();
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task UnknownDebits_AreListedAsUnmatched_ByDirection()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(5m, null);
        var debit = await UnknownDebitAsync(world);

        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin.AccessToken, "?status=Unmatched&direction=Debit"))
            .ShouldBe([debit]);
        (await StatementLineHttp.ListIdsAsync(world.TenantAdmin.AccessToken, "?status=Unmatched&direction=Credit"))
            .ShouldBe([receipt.PublicId]);
    }

    private static async Task<Guid> UnknownDebitAsync(DepositMatchingWorld world)
    {
        var sent = BankFeedHttp.Feed.AddReceipt(
            world.ClientMoney.AccountNumber, 7m, "USD", "Bank fee", direction: BankLineDirection.Debit);
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(sent.Id!);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
        return line.PublicId;
    }
}
