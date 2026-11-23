using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R15: the payer's bank recalls a funding line.</summary>
[Parallelizable]
public class MigrationBatchRecallTests : TestBase
{
    [Test]
    public async Task ARecall_OfAFundingLineOfADraft_ReturnsTheReceipt_AndTheBatchShowsTheFundingIssue()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var credit = await FundAsync(world, id, 250000m);

        var recallId = AddRecall(world, credit);
        await BankFeedHttp.RunAsync();

        var receipt = await BankFeedHttp.LineAsync(credit.OutsideId);
        (receipt.Status, receipt.MigrationBatchId, receipt.MatchMethod)
            .ShouldBe((BankStatementLineStatus.Returned, (int?)null, (StatementLineMatchMethod?)null));
        var recall = await BankFeedHttp.LineAsync(recallId);
        (recall.Status, recall.MatchMethod, recall.MatchedAccountId, recall.MatchedTransactionId, recall.RelatedLineId)
            .ShouldBe((BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Recall, (int?)null,
                (int?)null, (int?)receipt.Id));
        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Draft");
        Codes(item).ShouldBe(["FundingMismatch"]);
    }

    [Test]
    public async Task ARecall_OfAFundingLineOfASubmittedBatch_ReturnsTheReceipt_AndThePostIsRefused()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, credit) = await SubmittedAsync(world);

        AddRecall(world, credit);
        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(credit.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Returned);
        var post = await ActAsync(world.Tia, id, "post");
        (await ProblemResponse.ReadAsync(post)).Code.ShouldBe("migration_batch_not_valid");
        (await TransactionsOfBatchAsync(id)).ShouldBeEmpty();
    }

    [Test]
    public async Task ARecall_OfAFundingLineOfAPostedBatch_IsRecallNotCovered_AndPostsNothing()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, credit) = await PostedAsync(world);

        var recallId = AddRecall(world, credit);
        await BankFeedHttp.RunAsync();

        var recall = await BankFeedHttp.LineAsync(recallId);
        (recall.Status, recall.UnmatchedReason)
            .ShouldBe((BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.RecallNotCovered));
        var receipt = await BankFeedHttp.LineAsync(credit.OutsideId);
        (receipt.Status, receipt.MatchMethod, receipt.MigrationBatchId.HasValue)
            .ShouldBe((BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Migration, true));
        (await TransactionsOfBatchAsync(id)).Count.ShouldBe(4);
        (await Custody.CustodialHttp.CashBalanceAsync(world.TenantAdmin, world.AccountId)).ShouldBe(150000m);
    }

    private static string AddRecall(MigrationWorld world, BankStatementLine credit)
    {
        var id = "recall-" + Guid.NewGuid().ToString("N");
        BankFeedHttp.Feed.Add(world.Bank.ClientMoney.AccountNumber, new BankFeedLine(
            id, DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, credit.Amount, "USD", "ACME WEALTH", null, null,
            ReturnOf: credit.OutsideId, ReturnReason: "Payer's bank recalled the payment"));
        return id;
    }
}
