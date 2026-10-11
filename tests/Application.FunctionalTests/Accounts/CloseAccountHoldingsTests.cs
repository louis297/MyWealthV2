using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class CloseAccountHoldingsTests : TestBase
{
    [Test]
    public async Task Close_RejectsWhileAHoldingRemainsAndAllowsCloseAfterReverse()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenBrokerageAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, "VTI", "USD");
        var openingId = await ReadId(await TransactionHttp.Send(
            HttpMethod.Post,
            "/transactions",
            tokens.AccessToken,
            new
            {
                accountId,
                type = "OpeningHoldings",
                bookedAt = "2026-09-27T09:00:00+12:00",
                security = new[] { new { instrumentId, quantity = 10m, cost = 250m } }
            },
            Guid.NewGuid()));

        var rejected = await CloseAsync(accountId, tokens.AccessToken);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Open");

        (await TransactionHttp.Send(
            HttpMethod.Post,
            $"/transactions/{openingId}/reverse",
            tokens.AccessToken,
            body: null,
            Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CloseAsync(accountId, tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseMessage> CloseAsync(Guid accountId, string accessToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }

    private static async Task<Guid> OpenBrokerageAsync(string accessToken, Guid customerId)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = "Brokerage book",
            type = "Brokerage",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
