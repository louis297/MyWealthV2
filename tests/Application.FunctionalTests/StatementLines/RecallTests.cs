using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Withdrawals;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.StatementLines;

/// <summary>Bank-returns R3, R8: a recall or dishonour of a receipt, and credit lines with a <c>returnOf</c> the
/// platform cannot act on.</summary>
[Parallelizable]
public class RecallTests : TestBase
{
    [Test]
    public async Task ARecallOfACreditedReceipt_ReversesTheDeposit_AndReturnsTheReceipt()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).ShouldHaveSingleItem();
        var recallId = AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        var transactions = await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId);
        transactions.Count.ShouldBe(2);
        var reversal = transactions[1];
        reversal.Type.ShouldBe(TransactionType.Reversal);
        reversal.OriginalTransactionId.ShouldBe(deposit.Id);
        reversal.SourceKind.ShouldBe(TransactionSourceKind.BankFeed);
        reversal.BankReceiptDetail.ShouldNotBeNull();
        (await world.CashBalanceAsync()).ShouldBe(0m);
        var credit = await BankFeedHttp.LineAsync(receipt.OutsideId);
        credit.Status.ShouldBe(BankStatementLineStatus.Returned);
        credit.MatchedAccountId.ShouldBeNull();
        credit.MatchedTransactionId.ShouldBeNull();
        var recall = await BankFeedHttp.LineAsync(recallId);
        recall.Status.ShouldBe(BankStatementLineStatus.Matched);
        recall.MatchMethod.ShouldBe(StatementLineMatchMethod.Recall);
        recall.MatchedAccountId.ShouldBe(deposit.CashLegs.Single().AccountId);
        recall.MatchedTransactionId.ShouldBe(reversal.Id);
        recall.RelatedLineId.ShouldBe(receipt.Id);

        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, recall.PublicId);
        item.GetProperty("match").GetProperty("method").GetString().ShouldBe("Recall");
        item.GetProperty("match").GetProperty("accountId").GetGuid().ShouldBe(world.AccountId);
        item.GetProperty("match").GetProperty("transactionId").GetGuid().ShouldBe(reversal.PublicId);
        item.GetProperty("relatedLineId").GetGuid().ShouldBe(receipt.PublicId);
        item.GetProperty("returnReason").GetString().ShouldBe("Payer's bank recalled the payment");
    }

    [Test]
    public async Task ARecall_ForADisabledCustomer_StillReversesTheDeposit()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, true);
        AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).Count.ShouldBe(2);
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
    }

    [Test]
    public async Task ARecall_ProcessedTwice_PostsOneReversal()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();
        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).Count.ShouldBe(2);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task ARecall_WhenTheCashWasSpent_IsRecallNotCovered_AndPostsNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        (await CustodialHttp.WithdrawAsync(world.AccountId, 70m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var recallId = AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        await ShouldNotBeCoveredAsync(world, receipt, recallId, 30m);
    }

    [Test]
    public async Task ARecall_WhenTheCashIsReservedByAWithdrawal_IsRecallNotCovered()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        await WithdrawalDb.InsertAsync(world.AccountId, 60m, WithdrawalStatus.Requested);
        var recallId = AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        await ShouldNotBeCoveredAsync(world, receipt, recallId, 100m);
    }

    [Test]
    public async Task ARecall_OnAClosedAccount_IsRecallNotCovered()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        // An account with cash cannot be closed by hand, so the test closes it in the database.
        await CloseInDatabaseAsync(world.AccountId);
        var recallId = AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        await ShouldNotBeCoveredAsync(world, receipt, recallId, 100m);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ARecallOfAReceiptNeverCredited_ReturnsIt_WithNoTransaction(bool ignored)
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(40m, "No reference here");
        if (ignored)
        {
            (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, receipt.PublicId, "ignore", new() { ["note"] = "Test payment" }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var recallId = AddRecall(world, receipt.OutsideId, 40m);

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
        var recall = await BankFeedHttp.LineAsync(recallId);
        recall.Status.ShouldBe(BankStatementLineStatus.Matched);
        recall.MatchMethod.ShouldBe(StatementLineMatchMethod.Recall);
        recall.MatchedAccountId.ShouldBeNull();
        recall.MatchedTransactionId.ShouldBeNull();
        recall.RelatedLineId.ShouldBe(receipt.Id);
        (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).ShouldBeEmpty();
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, recall.PublicId);
        item.GetProperty("match").GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ARecallOfAReceiptToBeReturned_BeforeTheFirstAttempt_CancelsTheReturnPayment()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(40m, null);
        await RequestReturnAsync(world, receipt);
        AddRecall(world, receipt.OutsideId, 40m);

        await BankFeedHttp.PullAsync();
        await BankFeedHttp.ProcessAsync();

        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
        (await ReceiptReturnTests.ReturnPaymentsAsync(receipt.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Cancelled);
        await BankFeedHttp.SendAsync();
        BankFeedHttp.Payments.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task ARecallOfAReceiptToBeReturned_AfterAnAttempt_LeavesThePayment_AndItsLaterAnswerChangesOnlyThePayment()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(40m, null);
        await RequestReturnAsync(world, receipt);
        BankFeedHttp.Payments.Answer(BankPaymentResult.Failed(BankFeedFailure.Unavailable));
        await BankFeedHttp.SendAsync();
        var recallId = AddRecall(world, receipt.OutsideId, 40m);

        await BankFeedHttp.PullAsync();
        await BankFeedHttp.ProcessAsync();

        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
        (await BankFeedHttp.LineAsync(recallId)).MatchMethod.ShouldBe(StatementLineMatchMethod.Recall);
        (await ReceiptReturnTests.ReturnPaymentsAsync(receipt.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Pending);

        await BankFeedHttp.SendAsync();

        (await ReceiptReturnTests.ReturnPaymentsAsync(receipt.Id)).ShouldHaveSingleItem().Status.ShouldBe(OutgoingPaymentStatus.Accepted);
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
    }

    [Test]
    public async Task ASecondRecallOfOneReceipt_IsAnUnknownReturn()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        AddRecall(world, receipt.OutsideId, 100m);
        await BankFeedHttp.RunAsync();
        var second = AddRecall(world, receipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(second);
        (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).Count.ShouldBe(2);
    }

    [Test]
    public async Task ARecallOfAnotherAmount_IsAnUnknownReturn_AndTheDepositStays()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        var recallId = AddRecall(world, receipt.OutsideId, 90m);

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(recallId);
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
        (await world.CashBalanceAsync()).ShouldBe(100m);
    }

    [Test]
    public async Task ARecallOfAReceiptAlreadySentBack_IsAnUnknownReturn()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(40m, null);
        await RequestReturnAsync(world, receipt);
        await BankFeedHttp.SendAsync();
        var recallId = AddRecall(world, receipt.OutsideId, 40m);

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(recallId);
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.ReturnSent);
    }

    [Test]
    public async Task ARecallNamingALineThatIsItselfAReturn_IsAnUnknownReturn()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var lineId = "credit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            lineId, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 40m, "USD", "J SMITH", null, null,
            ReturnOf: "never-seen"));
        await BankFeedHttp.RunAsync();
        var recallId = AddRecall(world, lineId, 40m);

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(recallId);
    }

    [Test]
    public async Task ACreditWithAReturnOfThatNamesNoStoredLine_IsAnUnknownReturn_NotADeposit()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var lineId = "credit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            lineId, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 40m, "USD", "J SMITH", null,
            world.DepositReference, ReturnOf: "never-seen"));

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(lineId);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task ACreditThatReturnsTheDebitOfAReceiptReturn_IsAnUnknownReturn()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(40m, null);
        await RequestReturnAsync(world, receipt);
        await BankFeedHttp.SendAsync();
        var payment = (await ReceiptReturnTests.ReturnPaymentsAsync(receipt.Id)).ShouldHaveSingleItem();
        var debitId = "return-debit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            debitId, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, 40m, "USD", "J SMITH", null, null,
            EndToEndId: payment.InstructionPublicId.ToString(), ReturnOf: receipt.OutsideId));
        await BankFeedHttp.RunAsync();
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
        var againId = "credit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            againId, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 40m, "USD", "J SMITH", null, null,
            ReturnOf: debitId, ReturnReason: "Payer's account closed"));

        await BankFeedHttp.RunAsync();

        await ShouldBeUnknownReturnAsync(againId);
        var match = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, (await BankFeedHttp.LineAsync(againId)).PublicId, "match",
            new() { ["accountId"] = world.AccountId });
        match.StatusCode.ShouldBe(HttpStatusCode.NoContent, await match.Content.ReadAsStringAsync());
        (await world.CashBalanceAsync()).ShouldBe(40m);
    }

    [Test]
    public async Task ARecallNamingAnotherTenantsReceipt_IsAnUnknownReturn_AndLeavesThatReceipt()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var world = await DepositMatchingWorld.CreateAsync();
        var southReceipt = await south.ReceiveAsync(100m, south.DepositReference);
        var recallId = AddRecall(world, southReceipt.OutsideId, 100m);

        await BankFeedHttp.RunAsync();

        var stored = (await BankFeedHttp.LinesAsync(world.ClientMoney.Id)).SingleOrDefault(line => line.OutsideId == recallId);
        stored.ShouldNotBeNull();
        stored.RelatedLineId.ShouldBeNull();
        stored.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        stored.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReturn);
        (await BankFeedHttp.LineAsync(southReceipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
        (await south.CashBalanceAsync()).ShouldBe(100m);
    }

    [Test]
    public async Task ACreditNamingAnotherTenantsLine_IsAnUnknownReturn()
    {
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        var world = await DepositMatchingWorld.CreateAsync();
        var southReceipt = await south.ReceiveAsync(100m, south.DepositReference);
        var lineId = "credit-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, new BankFeedLine(
            lineId, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Credit, 100m, "USD", "J SMITH", null, null,
            ReturnOf: southReceipt.OutsideId));

        await BankFeedHttp.RunAsync();

        var stored = (await BankFeedHttp.LinesAsync(world.ClientMoney.Id)).SingleOrDefault(line => line.OutsideId == lineId);
        stored.ShouldNotBeNull();
        stored.RelatedLineId.ShouldBeNull();
        stored.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReturn);
    }

    [Test]
    public async Task UndoMatch_OfARecallLine_IsRefused()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var receipt = await world.ReceiveAsync(100m, world.DepositReference);
        var recallId = AddRecall(world, receipt.OutsideId, 100m);
        await BankFeedHttp.RunAsync();
        var recall = await BankFeedHttp.LineAsync(recallId);

        foreach (var action in new[] { "unmatch", "ignore", "reopen" })
        {
            Dictionary<string, object?> body = action == "reopen" ? new() : new() { ["note"] = "x" };
            var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, recall.PublicId, action, body);
            (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Status");
        }

        var returned = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, receipt.PublicId, "reopen");
        (await CustodialHttp.ErrorsAsync(returned)).ShouldContainKey("Status");
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    private static async Task ShouldNotBeCoveredAsync(DepositMatchingWorld world, BankStatementLine receipt, string recallId, decimal cash)
    {
        var recall = await BankFeedHttp.LineAsync(recallId);
        recall.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        recall.UnmatchedReason.ShouldBe(UnmatchedReason.RecallNotCovered);
        (await BankFeedHttp.TransactionsOfLineAsync(receipt.PublicId)).ShouldHaveSingleItem();
        (await BankFeedHttp.LineAsync(receipt.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
        (await world.CashBalanceAsync()).ShouldBe(cash);

        // R3: the TenantAdmin corrects the account with an adjustment, then ignores the line.
        var ignore = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, recall.PublicId, "ignore", new() { ["note"] = "Adjusted" });
        ignore.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ignore.Content.ReadAsStringAsync());
    }

    private static async Task CloseInDatabaseAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        account.Close();
        await db.SaveChangesAsync();
    }

    private static async Task ShouldBeUnknownReturnAsync(string outsideId)
    {
        var line = await BankFeedHttp.LineAsync(outsideId);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownReturn);
    }

    private static Task<HttpResponseMessage> RequestReturnAsync(DepositMatchingWorld world, BankStatementLine line) =>
        StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "request-return", new() { ["note"] = "Not our customer" });

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
