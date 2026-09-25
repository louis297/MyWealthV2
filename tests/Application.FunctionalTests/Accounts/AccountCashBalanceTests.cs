using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

public class AccountCashBalanceTests : TestBase
{
    [Test]
    public async Task GetAndList_IncludeComputedCashBalance()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);

        var before = await ReadAccount(tokens.AccessToken, accountId);
        before.GetProperty("currency").GetString().ShouldBe("NZD");
        before.GetProperty("cashBalance").GetDecimal().ShouldBe(0m);
        before.TryGetProperty("balance", out _).ShouldBeFalse();

        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 100.00m,
            bookedAt = "2026-09-24T09:00:00+12:00"
        }, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "TransferOut",
            amount = -25.00m,
            bookedAt = "2026-09-24T10:00:00+12:00"
        }, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var after = await ReadAccount(tokens.AccessToken, accountId);
        after.GetProperty("cashBalance").GetDecimal().ShouldBe(75.00m);

        var list = await AccountHttp.Send(HttpMethod.Get, "/accounts", tokens.AccessToken);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = listJson.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == accountId);
        item.GetProperty("cashBalance").GetDecimal().ShouldBe(75.00m);
        item.GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task CashBalanceRoute_ReturnsTheSameSum()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 40.00m,
            bookedAt = "2026-09-24T09:00:00+12:00"
        }, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var anonymous = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", accessToken: null);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.Location.ShouldBeNull();

        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        json.RootElement.GetProperty("currency").GetString().ShouldBe("NZD");
        json.RootElement.GetProperty("cashBalance").GetDecimal().ShouldBe(40.00m);

        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair-cash@north.example", "Password1!", tenant.Id);
        var otherTokens = await TestApp.IssueAccessTokenAsync(otherAdviser);
        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", otherTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        (await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", customerTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> ReadAccount(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
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
