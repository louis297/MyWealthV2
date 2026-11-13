using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

public class AssignPortfolioTests : TestBase
{
    [Test]
    public async Task Brokerage_AssignsAndBuysTheModel()
    {
        var (tokens, accountId, portfolioId) = await FundedBrokerageAsync("Assign Firm", "assign-firm");

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Success");

        var transactions = await PortfolioHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={accountId}", tokens.AccessToken);
        using var list = JsonDocument.Parse(await transactions.Content.ReadAsStringAsync());
        list.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ShouldContain("Buy");
    }

    [Test]
    public async Task SamePortfolioAgain_Returns409WithTheProblemCode()
    {
        var (tokens, accountId, portfolioId) = await FundedBrokerageAsync("Again Firm", "again-firm");
        (await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ProblemResponse.ReadAsync(response);
        problem.Code.ShouldBe("portfolio_already_assigned");
        problem.Detail.ShouldBe("Account already has this portfolio assigned.");
    }

    [Test]
    public async Task Bank_Returns400AndDoesNotAssign()
    {
        var (tokens, portfolioId, customerId, _) = await FundedContextAsync("Bank Firm", "bank-firm");
        var accountId = await CreateAccountAsync(tokens.AccessToken, customerId, "Bank", "Bank");

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/portfolio", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task MissingPrice_SavesFailedAssignmentAndWritesNoBuy()
    {
        var (tokens, accountId, portfolioId) = await FundedBrokerageAsync("Price Firm", "price-firm", withPrice: false);
        var before = await TransactionCountAsync(tokens.AccessToken, accountId);

        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("missing_price");
        (await TransactionCountAsync(tokens.AccessToken, accountId)).ShouldBe(before);

        var get = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/portfolio", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var assignment = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        assignment.RootElement.GetProperty("lastRebalanceStatus").GetString().ShouldBe("Failed");
    }

    private static async Task<(TokenResponse Tokens, Guid AccountId, Guid PortfolioId)> FundedBrokerageAsync(
        string tenantName,
        string tenantCode,
        bool withPrice = true)
    {
        var (tokens, portfolioId, customerId, instrumentId) = await FundedContextAsync(tenantName, tenantCode, withPrice);
        var accountId = await CreateAccountAsync(tokens.AccessToken, customerId, "Brokerage", "Model");
        var funded = await Transactions.TransactionHttp.Send(
            HttpMethod.Post,
            "/transactions",
            tokens.AccessToken,
            new
            {
                accountId,
                type = "TransferIn",
                amount = 10000m,
                bookedAt = "2026-01-02T00:00:00Z",
                currency = "NZD"
            },
            Guid.NewGuid());
        funded.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (tokens, accountId, portfolioId);
    }

    private static async Task<(TokenResponse Tokens, Guid PortfolioId, Guid CustomerId, Guid InstrumentId)> FundedContextAsync(
        string tenantName,
        string tenantCode,
        bool withPrice = true)
    {
        var (tenant, adviser, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            tenantName, tenantCode, tenantCode + "@firm", customerEmail: "customer-" + tenantCode + "@firm");
        _ = tenant;
        _ = adviser;
        var instrument = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "AIA",
            name = "AIA fund",
            quoteCurrency = "NZD"
        });
        using var instrumentJson = JsonDocument.Parse(await instrument.Content.ReadAsStringAsync());
        var instrumentId = instrumentJson.RootElement.GetProperty("id").GetGuid();
        if (withPrice)
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
        }

        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var portfolioJson = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync());
        return (tokens, portfolioJson.RootElement.GetProperty("id").GetGuid(), customer.PublicId, instrumentId);
    }

    private static async Task<Guid> CreateAccountAsync(string accessToken, Guid customerId, string type, string name)
    {
        var response = await Accounts.AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<int> TransactionCountAsync(string accessToken, Guid accountId)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/transactions?accountId={accountId}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("totalCount").GetInt32();
    }
}
