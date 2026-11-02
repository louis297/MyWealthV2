using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

public class RebalanceEnqueueTests : TestBase
{
    [Test]
    public async Task WeightChange_Returns202AndEnqueuesTheAccount()
    {
        var (_, _, customer, tokens) = await Accounts.AccountHttp.SignInAdviser(
            "Enqueue Firm", "enqueue-firm", "enqueue-firm@firm", customerEmail: "c-enqueue-firm@firm");
        var first = await CreateInstrumentAsync(tokens.AccessToken, "AIA");
        var second = await CreateInstrumentAsync(tokens.AccessToken, "FPH");
        await SetPriceAsync(first);
        await SetPriceAsync(second);
        var accountId = await CreateAccountAsync(tokens.AccessToken, customer.PublicId);
        (await Transactions.TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 10000m,
            bookedAt = "2026-01-02T00:00:00Z",
            currency = "NZD"
        }, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = first, targetWeight = 100m } }
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = createdJson.RootElement.GetProperty("id").GetGuid();
        (await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var before = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>().Enqueued.Count;
        var current = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{portfolioId}", tokens.AccessToken);
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{portfolioId}", tokens.AccessToken, new
        {
            name = "Growth",
            rowVersion = currentJson.RootElement.GetProperty("rowVersion").GetString(),
            lines = new[]
            {
                new { instrumentId = first, targetWeight = 50m },
                new { instrumentId = second, targetWeight = 50m }
            }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        accepted.RootElement.GetProperty("accounts").GetArrayLength().ShouldBe(1);
        accepted.RootElement.GetProperty("accounts")[0].GetProperty("accountId").GetGuid().ShouldBe(accountId);
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>().Enqueued.Count
            .ShouldBe(before + 1);

        var unassign = await PortfolioHttp.Send(HttpMethod.Delete, $"/accounts/{accountId}/portfolio", tokens.AccessToken);
        unassign.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var gone = await PortfolioHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/portfolio", tokens.AccessToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol)
    {
        var response = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task SetPriceAsync(Guid instrumentId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
    }

    private static async Task<Guid> CreateAccountAsync(string accessToken, Guid customerId)
    {
        var response = await Accounts.AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = "Model",
            type = "Brokerage",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
