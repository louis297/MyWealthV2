using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Custody modes R14, R15: portfolios are assigned to and rebalanced on Open Custodial accounts only.</summary>
[Parallelizable]
public class CustodialPortfolioTests : TestBase
{
    private const string AssignMessage = "Only an open Custodial account can be assigned a portfolio.";
    private const string RebalanceMessage = "The account is not an open Custodial account with an active portfolio.";

    [Test]
    public async Task Assign_ToAnOpenCustodialAccount_Returns200AndBuys()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await AssignAsync(tokens.AccessToken, accountId, portfolioId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await ActiveAssignmentCountAsync(accountId)).ShouldBe(1);
        (await TransactionTypesAsync(tokens.AccessToken, accountId)).ShouldContain("Buy");
    }

    [TestCase("Brokerage")]
    [TestCase("Other")]
    public async Task Assign_ToAnExternalAccount_Returns400AccountNotEligibleAndWritesNothing(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, type: type, currency: "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m, external: true)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var response = await AssignAsync(tokens.AccessToken, accountId, portfolioId);

        await ShouldBeNotEligibleAsync(response, AssignMessage);
        (await AssignmentCountAsync(accountId)).ShouldBe(0);
        (await TransactionCountAsync(accountId)).ShouldBe(1);
    }

    [Test]
    public async Task Assign_ToAClosedCustodialAccount_Returns400AccountNotEligible()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await CloseAsync(tokens.AccessToken, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await AssignAsync(tokens.AccessToken, accountId, portfolioId);

        await ShouldBeNotEligibleAsync(response, AssignMessage);
        (await AssignmentCountAsync(accountId)).ShouldBe(0);
    }

    [Test]
    public async Task Assign_ToAnotherTenantsCustodialAccount_Returns404()
    {
        var (_, _, otherCustomer, otherTokens) = await AccountHttp.SignInAdviser(
            "South Advisory", "south-advisory", "sam@south.example", customerEmail: "sue@south.example");
        var otherAccountId = await OpenCustodialAsync(otherTokens.AccessToken, otherCustomer.PublicId, "Theirs", "NZD");
        var (_, _, _, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);

        var response = await AssignAsync(tokens.AccessToken, otherAccountId, portfolioId);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AssignmentCountAsync(otherAccountId)).ShouldBe(0);
    }

    [Test]
    public async Task Rebalance_OfAnExternalAccountWithAnActiveAssignment_Returns400AccountNotEligible()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, currency: "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m, external: true)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        // As an assignment made before script 0032 ran.
        await InsertActiveAssignmentAsync(accountId, portfolioId);

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/rebalance", tokens.AccessToken);

        await ShouldBeNotEligibleAsync(response, RebalanceMessage);
        (await TransactionCountAsync(accountId)).ShouldBe(1);
    }

    [Test]
    public async Task Rebalance_OfAnAssignedCustodialAccount_Returns200()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken);
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AssignAsync(tokens.AccessToken, accountId, portfolioId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/rebalance", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AssignedCustodialAccount_RefusesManualTradesAndAllowsCash()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA", "NZD", 10m);
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken, instrumentId);
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AssignAsync(tokens.AccessToken, accountId, portfolioId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rebalanceBuy = await LatestBuyIdAsync(tokens.AccessToken, accountId);

        (await PostAsync(tokens.AccessToken, Trade(accountId, "Buy", instrumentId, 1m))).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(tokens.AccessToken, Trade(accountId, "Sell", instrumentId, 1m))).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await ReverseAsync(tokens.AccessToken, rebalanceBuy)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await FundAsync(tokens.AccessToken, accountId, 50m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var withdrawal = await WithdrawAsync(accountId, 50m);
        withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created, await withdrawal.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatePortfolioAsync(string accessToken, Guid? instrumentId = null)
    {
        var lineInstrument = instrumentId ?? await CreateInstrumentAsync(accessToken, "AIA", "NZD", 10m);
        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = lineInstrument, targetWeight = 100m } }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AssignAsync(string accessToken, Guid accountId, Guid portfolioId) =>
        PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", accessToken, new { portfolioId });

    private static async Task ShouldBeNotEligibleAsync(HttpResponseMessage response, string message)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var problem = await ProblemResponse.ReadAsync(response);
        problem.Code.ShouldBe("account_not_eligible");
        problem.Detail.ShouldBe(message);
    }

    private static async Task<List<string?>> TransactionTypesAsync(string accessToken, Guid accountId)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/transactions?accountId={accountId}", accessToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ToList();
    }

    private static async Task<Guid> LatestBuyIdAsync(string accessToken, Guid accountId)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/transactions?accountId={accountId}", accessToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("type").GetString() == "Buy")
            .GetProperty("id").GetGuid();
    }

    private static async Task<int> AssignmentCountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        return await db.AccountPortfolioAssignments.IgnoreQueryFilters().CountAsync(row => row.AccountId == account.Id);
    }

    private static async Task<int> ActiveAssignmentCountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        return await db.AccountPortfolioAssignments.CountAsync(
            row => row.AccountId == account.Id && row.Status == AssignmentStatus.Active);
    }

    private static async Task InsertActiveAssignmentAsync(Guid accountPublicId, Guid portfolioPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        var portfolio = await db.Portfolios.SingleAsync(row => row.PublicId == portfolioPublicId);
        var now = DateTimeOffset.UtcNow;
        var assignment = AccountPortfolioAssignment.Assign(null, account.TenantId, account.Id, portfolio.Id, "test", now);
        assignment.Created = now;
        assignment.CreatedBy = "test";
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }
}
