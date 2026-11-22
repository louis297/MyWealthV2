using System.Net;
using System.Text.Json;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R39, R40: the bank's interest on the trust account is a line of kind <c>Interest</c>, set
/// aside by the platform.</summary>
[Parallelizable]
public class BankInterestLineTests : TestBase
{
    private static BankFeedLine Line(DepositMatchingWorld world, BankLineDirection direction, string? reference = "Interest December") =>
        new("interest-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), direction, 520m, "USD",
            "Bank interest", null, reference, Kind: StatementLineKind.Interest);

    [Test]
    public async Task ACreditLineOfKindInterest_IsIgnoredWithTheNoteAndNoPerson_AndPostsNothing()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, Line(world, BankLineDirection.Credit));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(sent.Id!);
        line.Kind.ShouldBe(StatementLineKind.Interest);
        line.Status.ShouldBe(BankStatementLineStatus.Ignored);
        line.LastAction.ShouldBe(StatementLineAction.Ignored);
        line.LastActionNote.ShouldBe("Bank interest");
        line.LastActionByUserId.ShouldBeNull();
        line.LastActionAt.ShouldNotBeNull();
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();
        var item = await StatementLineHttp.GetAsync(world.TenantAdmin.AccessToken, line.PublicId);
        item.GetProperty("kind").GetString().ShouldBe("Interest");
        item.GetProperty("status").GetString().ShouldBe("Ignored");
        item.GetProperty("lastAction").GetProperty("note").GetString().ShouldBe("Bank interest");
        item.GetProperty("lastAction").GetProperty("by").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ACreditLineOfKindInterest_ThatQuotesADepositReference_IsStillSetAside()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, Line(world, BankLineDirection.Credit, world.DepositReference));

        await BankFeedHttp.RunAsync();

        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Ignored);
        (await world.CashBalanceAsync()).ShouldBe(0m);
    }

    [Test]
    public async Task ATenantAdmin_ReopensIt_AndItIsUnmatched()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, Line(world, BankLineDirection.Credit));
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(sent.Id!);

        var response = await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "reopen");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await BankFeedHttp.LineAsync(sent.Id!)).Status.ShouldBe(BankStatementLineStatus.Unmatched);
    }

    [Test]
    public async Task ADebitLineOfKindInterest_GoesToSuspense_AsAnUnknownPayment()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var sent = BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, Line(world, BankLineDirection.Debit));

        await BankFeedHttp.RunAsync();

        var line = await BankFeedHttp.LineAsync(sent.Id!);
        line.Status.ShouldBe(BankStatementLineStatus.Unmatched);
        line.UnmatchedReason.ShouldBe(UnmatchedReason.UnknownPayment);
    }

    [Test]
    public async Task TheList_FiltersByKind_AndAnUnknownKindIs400()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var interest = BankFeedHttp.Feed.Add(world.ClientMoney.AccountNumber, Line(world, BankLineDirection.Credit));
        var transfer = await world.ReceiveAsync(100m, world.DepositReference);
        await BankFeedHttp.RunAsync();
        var interestLine = await BankFeedHttp.LineAsync(interest.Id!);
        var token = world.TenantAdmin.AccessToken;

        (await StatementLineHttp.ListIdsAsync(token, "?kind=Interest")).ShouldBe([interestLine.PublicId]);
        (await StatementLineHttp.ListIdsAsync(token, "?kind=Transfer")).ShouldBe([transfer.PublicId]);
        var transferItem = await StatementLineHttp.GetAsync(token, transfer.PublicId);
        transferItem.GetProperty("kind").GetString().ShouldBe("Transfer");
        (await CustodialHttp.ErrorsAsync(await StatementLineHttp.Send(HttpMethod.Get, "/statement-lines?kind=Dividend", token)))
            .Keys.ShouldContain("Kind");
    }
}
