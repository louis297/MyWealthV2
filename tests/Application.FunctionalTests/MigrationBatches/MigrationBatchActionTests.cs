using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R6, R13, R14, R17–R19: submit, return and cancel, and what a submitted batch refuses.</summary>
[Parallelizable]
public class MigrationBatchActionTests : TestBase
{
    [Test]
    public async Task Submit_WithAnIssue_Returns400MigrationBatchNotValid_AndTheBatchStaysADraft()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        var response = await ActAsync(world.TenantAdmin, id, "submit");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ProblemResponse.ReadAsync(response);
        (problem.Code, problem.Detail).ShouldBe(("migration_batch_not_valid", "The migration batch has 1 issues."));
        (await GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Test]
    public async Task Submit_AValidBatch_IsSubmitted_WithThePersonAndTheTime()
    {
        var world = await MigrationWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (id, _) = await SubmittedAsync(world);

        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Submitted");
        item.GetProperty("submittedBy").GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        item.GetProperty("submittedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("validation").GetProperty("isValid").GetBoolean().ShouldBeTrue();
        Ids(await ListAsync(world.TenantAdmin, "?status=Submitted")).ShouldBe([id]);
    }

    [Test]
    public async Task ASubmittedBatch_RefusesSubmit_Replace_AndAFundingChange()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, line) = await SubmittedAsync(world);
        var other = await world.Bank.ReceiveAsync(5m, null);

        var submit = await ActAsync(world.TenantAdmin, id, "submit");
        var replace = await ReplaceAsync(world.TenantAdmin, id, world.ValidBody());
        var match = await MatchAsync(world.TenantAdmin, other.PublicId, id);
        var undo = await UnmatchAsync(world.TenantAdmin, line.PublicId);

        (await CustodialHttp.ErrorsAsync(submit))["Status"].ShouldBe(["This migration batch is Submitted and cannot be submitted."]);
        (await CustodialHttp.ErrorsAsync(replace))["Status"].ShouldBe(["Only a draft migration batch can be changed."]);
        (await CustodialHttp.ErrorsAsync(match))["MigrationBatchId"].ShouldBe(["Only a draft migration batch can be funded."]);
        (await CustodialHttp.ErrorsAsync(undo))["Status"].ShouldBe(["Return the migration batch to draft first."]);
        (await BankFeedHttp.LineAsync(line.OutsideId)).MigrationBatchId.ShouldNotBeNull();
        (await BankFeedHttp.LineAsync(other.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public async Task Return_WithoutANote_Returns400_AndWithOne_IsADraftWithTheReturn()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await SubmittedAsync(world);

        var missing = await ActAsync(world.Tia, id, "return", "  ");
        var response = await ActAsync(world.Tia, id, "return", " Wrong cost for VTI ");

        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("Note");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Draft");
        item.GetProperty("submittedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        var returned = item.GetProperty("returned");
        (returned.GetProperty("by").GetProperty("name").GetString(), returned.GetProperty("note").GetString())
            .ShouldBe(("Tia Admin", "Wrong cost for VTI"));
    }

    [Test]
    public async Task Return_BySubmitter_IsAllowed_AndFromADraft_Returns400()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await SubmittedAsync(world);

        (await ActAsync(world.TenantAdmin, id, "return", "My mistake")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.TenantAdmin, id, "return", "Again")))["Status"]
            .ShouldBe(["This migration batch is Draft and cannot be returned."]);
    }

    [Test]
    public async Task Cancel_ADraftWithAFundingLine_PutsTheLineBackInSuspense_AndEndsTheBatch()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var line = await FundAsync(world, id, 250000m);

        var response = await ActAsync(world.TenantAdmin, id, "cancel", "Duplicate of the November batch");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var stored = await BankFeedHttp.LineAsync(line.OutsideId);
        (stored.Status, stored.UnmatchedReason, stored.MigrationBatchId, stored.LastAction, stored.LastActionNote)
            .ShouldBe((BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.NoReference, (int?)null,
                (StatementLineAction?)StatementLineAction.Unmatched, "Duplicate of the November batch"));
        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Cancelled");
        item.GetProperty("validation").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("funding").GetArrayLength().ShouldBe(0);
        var cancelled = item.GetProperty("cancelled");
        cancelled.GetProperty("note").GetString().ShouldBe("Duplicate of the November batch");
        Ids(await ListAsync(world.TenantAdmin, "?status=Cancelled")).ShouldBe([id]);
        Ids(await ListAsync(world.TenantAdmin, "?status=Draft")).ShouldBeEmpty();
    }

    [Test]
    public async Task Cancel_ASubmittedBatch_IsCancelled_AndACancelledOne_RefusesEveryAction()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, line) = await SubmittedAsync(world);

        (await ActAsync(world.Tia, id, "cancel", "Not needed")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "cancel", "Again")))["Status"]
            .ShouldBe(["This migration batch is Cancelled and cannot be cancelled."]);
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "submit"))).ShouldContainKey("Status");
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "return", "Note"))).ShouldContainKey("Status");
        (await CustodialHttp.ErrorsAsync(await ReplaceAsync(world.Tia, id, world.ValidBody()))).ShouldContainKey("Status");
    }

    [TestCase("submit")]
    [TestCase("return")]
    [TestCase("cancel")]
    public async Task AnAction_WithAStaleRowVersion_Returns409_AndWithoutOne_Returns400(string action)
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await SubmittedAsync(world);

        var stale = await TenantHttp.Send(HttpMethod.Post, $"{MigrationBatchHttp.Path}/{id}/{action}", world.Tia,
            new { rowVersion = StaleRowVersion, note = "Note" });
        var missing = await TenantHttp.Send(HttpMethod.Post, $"{MigrationBatchHttp.Path}/{id}/{action}", world.Tia, new { note = "Note" });

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(stale)).Code.ShouldBe("stale_row_version");
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("RowVersion");
    }

    [Test]
    public async Task AFundingChange_MakesASubmitThatReadTheBatchBefore_Return409()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());
        var read = await RowVersionAsync(world.TenantAdmin, id);
        await FundAsync(world, id, 250000m);

        var submit = await TenantHttp.Send(HttpMethod.Post, $"{MigrationBatchHttp.Path}/{id}/submit", world.TenantAdmin,
            new { rowVersion = read });

        submit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
