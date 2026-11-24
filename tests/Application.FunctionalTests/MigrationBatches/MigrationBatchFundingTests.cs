using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R12–R14, R16, R30: a receipt matched to a batch is its funding, and its undo.</summary>
[Parallelizable]
public class MigrationBatchFundingTests : TestBase
{
    [Test]
    public async Task ABulkTransfer_WithNoReference_IsUnmatched_AndMatchingItToADraft_FundsTheBatch()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var before = await RowVersionAsync(world.TenantAdmin, id);
        var line = await world.Bank.ReceiveAsync(250000m, "ACME TRANSFER NOV");
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.NoReference);

        var response = await MatchAsync(world.TenantAdmin, line.PublicId, id);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin, line.PublicId);
        item.GetProperty("status").GetString().ShouldBe("Matched");
        item.GetProperty("migrationBatchId").GetGuid().ShouldBe(id);
        var match = item.GetProperty("match");
        match.GetProperty("method").GetString().ShouldBe("Migration");
        match.GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
        match.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        var batch = await GetAsync(world.TenantAdmin, id);
        batch.GetProperty("rowVersion").GetString().ShouldNotBe(before);
        var funding = batch.GetProperty("funding").EnumerateArray().Single();
        (funding.GetProperty("statementLineId").GetGuid(), funding.GetProperty("currency").GetString(),
            funding.GetProperty("amount").GetDecimal(), funding.GetProperty("reference").GetString())
            .ShouldBe((line.PublicId, "USD", 250000m, "ACME TRANSFER NOV"));
        batch.GetProperty("fundingTotals")[0].GetProperty("amount").GetDecimal().ShouldBe(250000m);
        batch.GetProperty("validation").GetProperty("isValid").GetBoolean().ShouldBeTrue();
        Codes(batch).ShouldBeEmpty();
    }

    [Test]
    public async Task AReceiptInAnotherAmount_LeavesAFundingIssue()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        await FundAsync(world, id, 200000m);

        Codes(await GetAsync(world.TenantAdmin, id)).ShouldBe(["FundingMismatch"]);
    }

    [Test]
    public async Task Match_WithBothAnAccountAndABatch_OrNeither_Returns400AtAccountId()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var line = await world.Bank.ReceiveAsync(250000m, null);

        var both = await StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, "match",
            new() { ["accountId"] = world.AccountId, ["migrationBatchId"] = id });
        var neither = await StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, "match", new());

        (await CustodialHttp.ErrorsAsync(both))["AccountId"].ShouldBe(["Give an account, a migration batch or a dividend."]);
        (await CustodialHttp.ErrorsAsync(neither))["AccountId"].ShouldBe(["Give an account, a migration batch or a dividend."]);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public async Task Match_ToAnotherTenantsBatch_OrAMissingOne_Returns404()
    {
        var world = await MigrationWorld.CreateAsync();
        var south = await MigrationWorld.CreateAsync("South Advisory", "south-advisory");
        var southBatch = await CreateIdAsync(south.TenantAdmin, south.ValidBody());
        var line = await world.Bank.ReceiveAsync(250000m, null);

        (await MatchAsync(world.TenantAdmin, line.PublicId, southBatch)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MatchAsync(world.TenantAdmin, line.PublicId, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public async Task Undo_OnADraft_PutsTheLineBackInSuspense_WithItsReason_AndTakesItOutOfTheBatch()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var line = await FundAsync(world, id, 250000m);
        var before = await RowVersionAsync(world.TenantAdmin, id);

        var response = await UnmatchAsync(world.TenantAdmin, line.PublicId);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var stored = await BankFeedHttp.LineAsync(line.OutsideId);
        (stored.Status, stored.UnmatchedReason, stored.MigrationBatchId, stored.MatchMethod)
            .ShouldBe((BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.NoReference, (int?)null,
                (StatementLineMatchMethod?)null));
        stored.LastAction.ShouldBe(StatementLineAction.Unmatched);
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        var batch = await GetAsync(world.TenantAdmin, id);
        batch.GetProperty("funding").GetArrayLength().ShouldBe(0);
        batch.GetProperty("rowVersion").GetString().ShouldNotBe(before);
        Codes(batch).ShouldBe(["FundingMismatch"]);
    }

    [TestCase("ignore")]
    [TestCase("request-return")]
    public async Task AFundingLine_AcceptsNoOtherAction(string action)
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var line = await FundAsync(world, id, 250000m);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin, line.PublicId, action, new() { ["note"] = "Note" });

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Status");
        (await BankFeedHttp.LineAsync(line.OutsideId)).MigrationBatchId.ShouldNotBeNull();
    }
}
