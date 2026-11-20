using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

[Parallelizable]
public class GetAccountPortfolioTests : TestBase
{
    [Test]
    public async Task Customer_ReadsOwnAccountPortfolio_OtherCustomerIs404()
    {
        var (tenant, adviser, customer, adviserTokens) = await Accounts.AccountHttp.SignInAdviser();
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var portfolioId = await CreatePortfolioAsync(adviserTokens.AccessToken);
        var ownId = await AssignedBrokerageAsync(adviserTokens.AccessToken, customer.PublicId, portfolioId, "Mine");
        var otherId = await AssignedBrokerageAsync(
            adviserTokens.AccessToken, otherCustomer.PublicId, portfolioId, "Theirs");

        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        (await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{ownId}/portfolio", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{otherId}/portfolio", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> CreatePortfolioAsync(string accessToken)
    {
        var instrument = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol = "AIA",
            name = "AIA fund",
            quoteCurrency = "NZD"
        });
        instrument.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var instrumentJson = JsonDocument.Parse(await instrument.Content.ReadAsStringAsync());
        var instrumentId = instrumentJson.RootElement.GetProperty("id").GetGuid();
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
        }

        var portfolio = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var portfolioJson = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync());
        return portfolioJson.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AssignedBrokerageAsync(
        string accessToken, Guid customerId, Guid portfolioId, string name)
    {
        var accountId = await Accounts.AccountHttp.OpenAccountAsync(accessToken, customerId, name, type: "Brokerage", custodyMode: "Custodial");

        var funded = await Custody.CustodialHttp.FundAsync(accessToken, accountId, 10000m);
        funded.StatusCode.ShouldBe(HttpStatusCode.Created);

        var assign = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/portfolio", accessToken, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK);
        return accountId;
    }
}
