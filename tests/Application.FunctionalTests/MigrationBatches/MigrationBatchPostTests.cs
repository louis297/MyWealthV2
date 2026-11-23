using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.MigrationBatches.MigrationBatchHttp;

namespace MyWealthV2.Application.FunctionalTests.MigrationBatches;

/// <summary>Migration batches R20–R25, R27, R31: the post by a second person, what it writes, and the reads.</summary>
[Parallelizable]
public class MigrationBatchPostTests : TestBase
{
    [Test]
    public async Task Post_ByThePersonWhoSubmitted_Returns400_AndTheBatchStaysSubmitted()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await SubmittedAsync(world);

        var response = await ActAsync(world.TenantAdmin, id, "post");

        (await CustodialHttp.ErrorsAsync(response))["Status"]
            .ShouldBe(["A migration batch must be posted by someone other than the person who submitted it."]);
        (await GetAsync(world.TenantAdmin, id)).GetProperty("status").GetString().ShouldBe("Submitted");
        (await TransactionsOfBatchAsync(id)).ShouldBeEmpty();
    }

    [Test]
    public async Task Post_FromADraft_Returns400AtStatus()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, world.ValidBody());

        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "post")))["Status"]
            .ShouldBe(["This migration batch is Draft and cannot be posted."]);
    }

    [Test]
    public async Task Post_ByASecondTenantAdmin_WritesAnOpeningAndAnOpeningHoldingsPerEntry_AndThePositions()
    {
        var world = await MigrationWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (id, _) = await PostedAsync(world);

        var written = await TransactionsOfBatchAsync(id);
        written.Select(t => (t.Type, t.CashLegs.SingleOrDefault()?.AccountId ?? t.SecurityLegs.Single().AccountId)).Count().ShouldBe(4);
        written.Select(t => t.Type).ShouldBe([TransactionType.Opening, TransactionType.OpeningHoldings, TransactionType.Opening,
            TransactionType.OpeningHoldings]);
        written.Select(t => t.BookedAt).Distinct().ShouldHaveSingleItem().ShouldBeGreaterThan(before);
        written.ShouldAllBe(t => t.Memo == "Opening balance" && t.MigrationDetail != null
            && t.MigrationDetail.AsOfDate == new DateOnly(2026, 11, 20) && t.MigrationDetail.PreviousProvider == "Acme Wealth");
        written[0].Reference.ShouldBe("ACME-" + world.AccountId.ToString("N")[..6]);
        written[0].CashLegs.Single().Amount.ShouldBe(150000m);
        (written[1].SecurityLegs.Single().Quantity, written[1].SecurityLegs.Single().CostAmount).ShouldBe((800m, 176000m));
        var tia = await IdentityUserIdAsync("tia@north-advisory.example");
        written.ShouldAllBe(t => t.CreatedBy == tia);

        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin, world.AccountId)).ShouldBe(150000m);
        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin, world.SecondAccountId)).ShouldBe(100000m);
        var balance = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{world.AccountId}/cash-balance", world.TenantAdmin);
        using (var json = JsonDocument.Parse(await balance.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("availableCash").GetDecimal().ShouldBe(150000m);
            json.RootElement.GetProperty("withdrawableCash").GetDecimal().ShouldBe(150000m);
        }

        var holdings = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{world.SecondAccountId}/holdings", world.TenantAdmin);
        var holdingsContent = await holdings.Content.ReadAsStringAsync();
        holdings.StatusCode.ShouldBe(HttpStatusCode.OK, holdingsContent);
        using (var json = JsonDocument.Parse(holdingsContent))
        {
            var holding = json.RootElement.GetProperty("items").EnumerateArray().Single();
            (holding.GetProperty("instrumentId").GetGuid(), holding.GetProperty("quantity").GetDecimal())
                .ShouldBe((world.InstrumentId, 400m));
        }

        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Posted");
        item.GetProperty("postedBy").GetProperty("name").GetString().ShouldBe("Tia Admin");
        item.GetProperty("validation").ValueKind.ShouldBe(JsonValueKind.Null);
        var entry = item.GetProperty("entries")[0];
        entry.GetProperty("openingTransactionId").GetGuid().ShouldBe(written[0].PublicId);
        entry.GetProperty("openingHoldingsTransactionId").GetGuid().ShouldBe(written[1].PublicId);
        Ids(await ListAsync(world.TenantAdmin, "?status=Posted")).ShouldBe([id]);
    }

    [Test]
    public async Task AnEntryWithCashOnly_OrHoldingsOnly_WritesOneTransaction()
    {
        var world = await MigrationWorld.CreateAsync();
        var id = await CreateIdAsync(world.TenantAdmin, Body(
            [Entry(world.AccountId, 100m), Entry(world.SecondAccountId, null, Holding(world.InstrumentId, 3m, 600m))],
            Totals(2, [Money("USD", 100m)], [Quantity(world.InstrumentId, 3m)], [Money("USD", 600m)])));
        await FundAsync(world, id, 100m);
        (await ActAsync(world.TenantAdmin, id, "submit")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ActAsync(world.Tia, id, "post")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TransactionsOfBatchAsync(id)).Select(t => t.Type).ShouldBe([TransactionType.Opening, TransactionType.OpeningHoldings]);
        var entries = (await GetAsync(world.TenantAdmin, id)).GetProperty("entries");
        entries[0].GetProperty("openingHoldingsTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        entries[1].GetProperty("openingTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ADepositThatReachesAnAccountAfterSubmit_StopsThePost_AndNothingIsWritten()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await SubmittedAsync(world);
        (await CustodialHttp.FundAsync(world.TenantAdmin, world.SecondAccountId, 10m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await ActAsync(world.Tia, id, "post");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("migration_batch_not_valid");
        (await TransactionsOfBatchAsync(id)).ShouldBeEmpty();
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        var item = await GetAsync(world.TenantAdmin, id);
        item.GetProperty("status").GetString().ShouldBe("Submitted");
        Codes(item).ShouldBe(["AccountNotEmpty"]);
    }

    [Test]
    public async Task ASecondPost_WithTheNewRowVersion_Returns400_AndWritesNothingMore()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await PostedAsync(world);

        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "post")))["Status"]
            .ShouldBe(["This migration batch is Posted and cannot be posted."]);
        (await TransactionsOfBatchAsync(id)).Count.ShouldBe(4);
    }

    [Test]
    public async Task APostedBatch_RefusesTheUndoOfItsFundingLine_AndEveryAction()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, line) = await PostedAsync(world);

        (await CustodialHttp.ErrorsAsync(await UnmatchAsync(world.TenantAdmin, line.PublicId)))["Status"]
            .ShouldBe(["The migration batch is posted, so this receipt cannot be taken out of it."]);
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "cancel", "Too late"))).ShouldContainKey("Status");
        (await CustodialHttp.ErrorsAsync(await ActAsync(world.Tia, id, "return", "Too late"))).ShouldContainKey("Status");
        (await BankFeedHttp.LineAsync(line.OutsideId)).Status.ShouldBe(BankStatementLineStatus.Matched);
    }

    [Test]
    public async Task AHandReverse_OfAMigratedOpening_Returns400()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await PostedAsync(world);
        var opening = (await TransactionsOfBatchAsync(id))[0];

        var response = await CustodialHttp.ReverseAsync(world.TenantAdmin, opening.PublicId);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("cannot be reversed by hand");
        (await CustodialHttp.CashBalanceAsync(world.TenantAdmin, world.AccountId)).ShouldBe(150000m);
    }

    [Test]
    public async Task TheAdviserAndTheCustomer_ReadTheTransactions_WithTheMigration_ButNotTheBatch()
    {
        var world = await MigrationWorld.CreateAsync();
        var (id, _) = await PostedAsync(world);
        var opening = (await TransactionsOfBatchAsync(id))[0];

        foreach (var token in new[] { world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            using var item = await CustodialHttp.GetTransactionAsync(token, opening.PublicId);
            var root = item.RootElement;
            (root.GetProperty("type").GetString(), root.GetProperty("memo").GetString()).ShouldBe(("Opening", "Opening balance"));
            var source = root.GetProperty("source");
            (source.GetProperty("kind").GetString(), source.GetProperty("id").GetGuid()).ShouldBe(("Migration", id));
            var migration = root.GetProperty("migration");
            (migration.GetProperty("asOfDate").GetString(), migration.GetProperty("previousProvider").GetString())
                .ShouldBe(("2026-11-20", "Acme Wealth"));
            (await TenantHttp.Send(HttpMethod.Get, $"{MigrationBatchHttp.Path}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var deposit = await world.Bank.ReceiveAsync(5m, world.Bank.DepositReference);
        var other = (await BankFeedHttp.TransactionsOfLineAsync(deposit.PublicId)).Single();
        using var plain = await CustodialHttp.GetTransactionAsync(world.TenantAdmin, other.PublicId);
        plain.RootElement.GetProperty("migration").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task AnAccountWithAnActiveAssignment_IsPosted_AndNoRebalanceIsProposed()
    {
        var world = await MigrationWorld.CreateAsync();
        await AssignPortfolioAsync(world.TenantAdmin, world.AccountId, world.InstrumentId);
        var rebalances = await RebalanceCountAsync();
        var (id, _) = await SubmittedAsync(world);

        (await ActAsync(world.Tia, id, "post")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TransactionsOfBatchAsync(id)).Count.ShouldBe(4);
        (await RebalanceCountAsync()).ShouldBe(rebalances);
    }

    private static async Task AssignPortfolioAsync(string accessToken, Guid accountId, Guid instrumentId)
    {
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "USD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = json.RootElement.GetProperty("id").GetGuid();
        var assigned = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", accessToken, new { portfolioId });
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
    }

    private static async Task<int> RebalanceCountAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Rebalances.IgnoreQueryFilters().CountAsync();
    }

    private static async Task<string> IdentityUserIdAsync(string email)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Email == email)).IdentityUserId;
    }
}
