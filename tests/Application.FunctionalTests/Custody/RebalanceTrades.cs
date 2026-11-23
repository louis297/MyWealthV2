using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Domain.Entities;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>A funded Custodial account with a portfolio, and its rebalance trades (transaction-source R5, R9).</summary>
internal static class RebalanceTrades
{
    public sealed record World(string AccessToken, User Customer, Guid AccountId, Guid PortfolioId, Guid First, Guid Second);

    /// <summary>Opens and funds a Custodial NZD account, assigns a 100 % <c>AIA</c> portfolio and completes the rebalance
    /// it proposes, which buys through orders.</summary>
    public static async Task<World> AssignedAsync(
        string firm = "Rebalance Source",
        string code = "rebalance-source",
        string email = "adviser@rebalance-source.example",
        string customerEmail = "customer@rebalance-source.example")
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser(firm, code, email, customerEmail: customerEmail);
        var first = await CreateInstrumentAsync(tokens.AccessToken, "AIA", "NZD", 10m);
        var second = await CreateInstrumentAsync(tokens.AccessToken, "FPH", "NZD", 20m);
        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", tokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = first, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var portfolioId = createdJson.RootElement.GetProperty("id").GetGuid();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var assign = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new { portfolioId });
        assign.StatusCode.ShouldBe(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        // Rebalances R10, R26: assign proposes; the buys are the fills of the rebalance's orders.
        await Rebalances.RebalanceHttp.CompleteFromAsync(tokens.AccessToken, assign);
        return new World(tokens.AccessToken, customer, accountId, portfolioId, first, second);
    }

    /// <summary>Changes the portfolio to 50 % each line; the 202 enqueues a rebalance the test scheduler only records.</summary>
    public static async Task<JsonDocument> SplitHalfAndHalfAsync(World world)
    {
        var current = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{world.PortfolioId}", world.AccessToken);
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        var response = await PortfolioHttp.Send(HttpMethod.Put, $"/portfolios/{world.PortfolioId}", world.AccessToken, new
        {
            name = "Growth",
            rowVersion = currentJson.RootElement.GetProperty("rowVersion").GetString(),
            lines = new[]
            {
                new { instrumentId = world.First, targetWeight = 50m },
                new { instrumentId = world.Second, targetWeight = 50m }
            }
        });
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, content);
        return JsonDocument.Parse(content);
    }

    /// <summary>The <c>Buy</c> and <c>Sell</c> items on the account, newest first.</summary>
    public static async Task<List<JsonElement>> TradesAsync(string accessToken, Guid accountId)
    {
        var response = await PortfolioHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={accountId}&pageSize=100", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() is "Buy" or "Sell")
            .Select(item => item.Clone())
            .ToList();
    }
}
