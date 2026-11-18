using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Portfolios;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>
/// Tenant custody modes R9: narrowing a Tenant's allowed custody modes changes no existing account. Every route
/// on an existing Custodial account keeps working; only a new Custodial open is refused.
/// </summary>
public class TenantCustodyModesNarrowingTests : TestBase
{
    private const string TransferPath = "/transactions/transfer";

    [Test]
    public async Task ExistingCustodialAccounts_KeepWorkingAfterTheTenantStopsAllowingCustodial()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "AIA", "NZD", 10m);
        var portfolioId = await CreatePortfolioAsync(tokens.AccessToken, instrumentId);
        var assigned = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Model", "NZD");
        var trading = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Trading", "NZD");
        var spare = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "Spare", "NZD");
        (await FundAsync(tokens.AccessToken, assigned, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{assigned}/portfolio", tokens.AccessToken,
            new { portfolioId })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await NarrowToExternalAsync(tenant);

        (await FundAsync(tokens.AccessToken, assigned, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await FundAsync(tokens.AccessToken, trading, 500m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var buy = await PostAsync(tokens.AccessToken, Trade(trading, "Buy", instrumentId, 5m));
        buy.StatusCode.ShouldBe(HttpStatusCode.Created, await buy.Content.ReadAsStringAsync());
        var transfer = await PostAsync(tokens.AccessToken, TransferBody(trading, spare, 40m), TransferPath);
        transfer.StatusCode.ShouldBe(HttpStatusCode.Created, await transfer.Content.ReadAsStringAsync());
        var rebalance = await PortfolioHttp.Send(
            HttpMethod.Post, $"/accounts/{assigned}/rebalance", tokens.AccessToken);
        rebalance.StatusCode.ShouldBe(HttpStatusCode.OK, await rebalance.Content.ReadAsStringAsync());

        var closeOut = await PostAsync(tokens.AccessToken, Cash(spare, "CloseOut", -40m));
        closeOut.StatusCode.ShouldBe(HttpStatusCode.Created, await closeOut.Content.ReadAsStringAsync());
        (await CloseAsync(tokens.AccessToken, spare)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReopenAsync(tokens.AccessToken, spare)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CustodialAccountIdsAsync(tokens.AccessToken)).ShouldBe([assigned, trading, spare], ignoreOrder: true);

        var open = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken, new
        {
            customerId = customer.PublicId,
            name = "Another managed account",
            type = "Brokerage",
            custodyMode = "Custodial",
            currency = "NZD"
        });
        (await ErrorsAsync(open))["CustodyMode"].ShouldContain("The tenant does not allow Custodial accounts.");
    }

    private static async Task NarrowToExternalAsync(Tenant tenant)
    {
        var admin = await TenantHttp.SignInSystemAdmin();
        var get = await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}", admin.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var put = await TenantHttp.Send(HttpMethod.Put, $"/tenants/{tenant.PublicId}", admin.AccessToken, new
        {
            name = json.RootElement.GetProperty("name").GetString(),
            allowedCustodyModes = new[] { "External" },
            rowVersion = json.RootElement.GetProperty("rowVersion").GetString()
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> ReopenAsync(string accessToken, Guid accountId)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/reopen", accessToken, new { rowVersion });
    }

    private static async Task<List<Guid>> CustodialAccountIdsAsync(string accessToken)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, "/accounts?custodyMode=Custodial", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task<Guid> CreatePortfolioAsync(string accessToken, Guid instrumentId)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", accessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId, targetWeight = 100m } }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Dictionary<string, object?> TransferBody(Guid fromAccountId, Guid toAccountId, decimal amount) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["amount"] = amount
    };
}
