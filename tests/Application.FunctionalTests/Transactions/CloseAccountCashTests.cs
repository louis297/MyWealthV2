using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class CloseAccountCashTests : TestBase
{
    [Test]
    public async Task Close_RejectsWhileCashSumIsNotZero()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        (await PostAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var close = await CloseAsync(accountId, tokens.AccessToken);
        close.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", tokens.AccessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Open");
    }

    [Test]
    public async Task CloseOut_ZeroesTheBookSoTheAccountCanClose()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        (await PostAsync(tokens.AccessToken, accountId, "TransferIn", 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, accountId, "CloseOut", -40m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(tokens.AccessToken, accountId, "CloseOut", -100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, accountId, "CloseOut", -1m)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await CloseAsync(accountId, tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Close_AllowsAnAccountThatWasNeverBooked()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);

        (await CloseAsync(accountId, tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Guid accountId,
        string type,
        decimal amount)
    {
        return await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            amount,
            bookedAt = "2026-09-24T09:00:00+12:00"
        }, Guid.NewGuid());
    }

    private static async Task<HttpResponseMessage> CloseAsync(Guid accountId, string accessToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = "Everyday spending",
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
