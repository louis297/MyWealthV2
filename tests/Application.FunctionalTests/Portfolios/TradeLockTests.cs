using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

[Parallelizable]
public class TradeLockTests : TestBase
{
    [Test]
    public async Task AssignedAccount_RejectsManualBuyAndAllowsCashIn()
    {
        var (tokens, accountId, instrumentId) = await AssignedAccountAsync("Lock Firm", "lock-firm");

        var cash = await Custody.CustodialHttp.FundAsync(tokens.AccessToken, accountId, 50m);
        cash.StatusCode.ShouldBe(HttpStatusCode.Created);

        var buy = await Transactions.TransactionHttp.Send(
            HttpMethod.Post,
            "/transactions",
            tokens.AccessToken,
            new
            {
                accountId,
                type = "Buy",
                security = new[] { new { instrumentId, quantity = 1m } }
            },
            Guid.NewGuid());
        buy.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AssignedAccount_RejectsReverseOfASecurityTrade()
    {
        var (tokens, accountId, instrumentId) = await FundedAccountAsync("Reverse Lock", "reverse-lock");
        // A trade typed before orders (orders R48), which a person could reverse were the account not assigned.
        var buyId = await Custody.CustodialHttp.InsertManualTradeAsync(accountId, "Buy", instrumentId, 1m, price: 10m);

        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken, instrumentId);
        var assign = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK);

        var reverse = await Transactions.TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{buyId}/reverse", tokens.AccessToken, idempotencyKey: Guid.NewGuid());
        reverse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<(TokenResponse Tokens, Guid AccountId, Guid InstrumentId)> AssignedAccountAsync(
        string name, string code)
    {
        var setup = await FundedAccountAsync(name, code);
        var portfolioId = await CreatePortfolioAsync(setup.Tokens.AccessToken, setup.InstrumentId);
        var assign = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{setup.AccountId}/portfolio", setup.Tokens.AccessToken, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK);
        return setup;
    }

    private static async Task<(TokenResponse Tokens, Guid AccountId, Guid InstrumentId)> FundedAccountAsync(
        string name, string code)
    {
        var (_, _, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            name, code, code + "@firm", customerEmail: "c-" + code + "@firm");
        var instrument = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", tokens.AccessToken, new
        {
            symbol = "AIA",
            name = "AIA fund",
            quoteCurrency = "NZD"
        });
        using var instrumentJson = JsonDocument.Parse(await instrument.Content.ReadAsStringAsync());
        var instrumentId = instrumentJson.RootElement.GetProperty("id").GetGuid();
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
        }

        var accountId = await Accounts.AccountHttp.OpenAccountAsync(
            tokens.AccessToken, customer.PublicId, name: "Model", type: "Brokerage", custodyMode: "Custodial");
        var funded = await Custody.CustodialHttp.FundAsync(tokens.AccessToken, accountId, 10000m);
        funded.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (tokens, accountId, instrumentId);
    }

    private static async Task<Guid> CreatePortfolioAsync(string accessToken, Guid instrumentId)
    {
        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
