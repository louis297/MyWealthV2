using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.InKindTransfers;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R18–R20, R29, R49, R51: which entitlements reinvest, and the election of a dividend.</summary>
[Parallelizable]
public class DividendReinvestmentTests : TestBase
{
    private const decimal Plan = 25m;

    internal static async Task ElectAsync(NominatedWorld world, Guid accountId, bool reinvest = true)
    {
        var token = world.TenantAdmin.AccessToken;
        var rowVersion = (await AccountClosureHttp.AccountAsync(token, accountId)).GetProperty("rowVersion").GetString();
        (await AccountHttp.Send(HttpMethod.Put, $"/accounts/{accountId}/dividend-reinvestment", token, new { reinvest, rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>A plan dividend; Ned elects to reinvest and the Tenant has a USD safekeeping account, unless told
    /// otherwise.</summary>
    private static Task<DividendWorld> PlanAsync(bool safekeeping = true, Func<NominatedWorld, Holders, Task>? more = null) =>
        DividendWorld.CreateAsync(price: Plan, setUp: async (world, holders) =>
        {
            if (safekeeping)
            {
                await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");
            }

            await ElectAsync(world, holders.Ned);
            if (more is not null)
            {
                await more(world, holders);
            }
        });

    [Test]
    public async Task AnElectingAccount_Reinvests_WithItsParticipatingQuantity_AndTheDividendHasAPendingElection()
    {
        var dividend = await PlanAsync();

        var entitlements = await EntitlementsAsync(dividend.DividendId);
        var ned = entitlements.Single(row => row.AccountId == AccountIdOf(dividend.Holders.Ned));
        (ned.Reinvests, ned.ParticipatingQuantity).ShouldBe((true, (decimal?)78.82352941m));
        var olga = entitlements.Single(row => row.AccountId == AccountIdOf(dividend.Holders.Olga));
        (olga.Reinvests, olga.ParticipatingQuantity).ShouldBe((false, (decimal?)null));

        var action = await dividend.DividendAsync();
        (action.ElectionStatus, action.ElectionQuantity).ShouldBe(
            ((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.Pending, (decimal?)78.82352941m));
        action.SafekeepingAccountId.ShouldNotBeNull();

        var election = (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election");
        election.GetProperty("status").GetString().ShouldBe("Pending");
        election.GetProperty("quantity").GetDecimal().ShouldBe(78.82352941m);
        election.GetProperty("attempts").GetInt32().ShouldBe(0);
        election.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        election.GetProperty("sharesReceived").ValueKind.ShouldBe(JsonValueKind.Null);
        election.GetProperty("releasedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        var item = (await GetAsync(dividend.TenantAdmin, $"{Root}/{dividend.DividendId}/entitlements")).GetProperty("items")
            .EnumerateArray().Single(row => row.GetProperty("accountId").GetGuid() == dividend.Holders.Ned);
        (item.GetProperty("reinvests").GetBoolean(), item.GetProperty("participatingQuantity").GetDecimal()).ShouldBe((true, 78.82352941m));
    }

    [Test]
    public async Task WithAPendingElection_TheReceiptAloneDoesNotPay()
    {
        var dividend = await PlanAsync();

        // Until the custodian accepts the election, the expected amount is the whole net of foreign tax.
        var line = await dividend.ReceiveAsync(93.50m);

        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Dividend);
        (await dividend.DividendAsync()).PaidAt.ShouldBeNull();
    }

    [Test]
    public async Task ADividendWithNoPlan_HasNoElection()
    {
        var dividend = await DividendWorld.CreateAsync(setUp: async (world, holders) =>
        {
            await SafekeepingHttp.EnsureActiveAsync(world.Tenant.Id, "USD");
            await ElectAsync(world, holders.Ned);
        });

        (await dividend.DividendAsync()).ElectionStatus.ShouldBe(ReinvestmentElectionStatus.None);
        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => !row.Reinvests);
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task ATenantWithNoSafekeepingAccount_ReinvestsNothing()
    {
        var dividend = await PlanAsync(safekeeping: false);

        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => !row.Reinvests);
        var action = await dividend.DividendAsync();
        (action.ElectionStatus, action.ElectionQuantity).ShouldBe(((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.None, (decimal?)null));
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("election").GetProperty("status").GetString().ShouldBe("None");
    }

    [Test]
    public async Task AnAccountThatFollowsAPortfolio_OrIsInAnotherCurrency_DoesNotReinvest()
    {
        var nzd = Guid.Empty;
        var dividend = await PlanAsync(more: async (world, holders) =>
        {
            await AssignAsync(holders.Ned);
            nzd = await Interest.InterestHttp.OpenCustodialAsync(world.Tenant.Id, world.TenantAdmin.AccessToken, world.Customer.PublicId, "NZD");
            (await CustodialHttp.FundAsync(world.TenantAdmin.AccessToken, nzd, 2000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
            await CustodialHttp.InsertManualTradeAsync(nzd, "Buy", holders.InstrumentId, 20m);
            await ElectAsync(world, nzd);
        });

        (await EntitlementsAsync(dividend.DividendId)).ShouldAllBe(row => !row.Reinvests);
        (await dividend.DividendAsync()).ElectionStatus.ShouldBe(ReinvestmentElectionStatus.None);
    }

    [Test]
    public async Task AChangeOfTheElectionAfterTheExDate_DoesNotChangeTheEntitlement()
    {
        var dividend = await PlanAsync();

        await ElectAsync(dividend.World, dividend.Holders.Ned, reinvest: false);
        await ElectAsync(dividend.World, dividend.Holders.Olga);

        var entitlements = await EntitlementsAsync(dividend.DividendId);
        entitlements.Single(row => row.AccountId == AccountIdOf(dividend.Holders.Ned)).Reinvests.ShouldBeTrue();
        entitlements.Single(row => row.AccountId == AccountIdOf(dividend.Holders.Olga)).Reinvests.ShouldBeFalse();
    }

    [Test]
    public async Task AnOverseasCustomer_OnAForeignDividend_ReinvestsTheWholeQuantity()
    {
        var dividend = await PlanAsync(more: (world, _) =>
            Interest.TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Overseas", true, null));

        (await EntitlementsAsync(dividend.DividendId)).Single(row => row.AccountId == AccountIdOf(dividend.Holders.Ned))
            .ParticipatingQuantity.ShouldBe(100m);
    }

    private static async Task AssignAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var instrument = await db.Instruments.IgnoreQueryFilters().FirstAsync(row => row.TenantId == account.TenantId);
        var usd = await db.Set<Currency>().SingleAsync(row => row.Code == "USD");
        var portfolio = Portfolio.Create(account.TenantId, "Growth", usd, [(instrument.Id, 100m)]);
        portfolio.CreatedBy = "test";
        db.Portfolios.Add(portfolio);
        await db.SaveChangesAsync();
        var assignment = AccountPortfolioAssignment.Assign(null, account.TenantId, account.Id, portfolio.Id, "test", DateTimeOffset.UtcNow);
        assignment.Created = DateTimeOffset.UtcNow;
        assignment.CreatedBy = "test";
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    private static int AccountIdOf(Guid accountId) => AccountIdAsync(accountId).GetAwaiter().GetResult();
}
