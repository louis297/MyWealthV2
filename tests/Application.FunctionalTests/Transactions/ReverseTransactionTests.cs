using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class ReverseTransactionTests : TestBase
{
    [Test]
    public async Task Reverse_CreatesANewOppositeAndLeavesTheOriginalUntouched()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var originalId = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 100m));

        var response = await ReverseAsync(tokens.AccessToken, originalId);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var reversalId = created.RootElement.GetProperty("id").GetGuid();
        reversalId.ShouldNotBe(originalId);

        var original = await ReadItem(tokens.AccessToken, originalId);
        original.GetProperty("type").GetString().ShouldBe("TransferIn");
        original.GetProperty("amount").GetDecimal().ShouldBe(100m);
        original.GetProperty("originalTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);

        var reversal = await ReadItem(tokens.AccessToken, reversalId);
        reversal.GetProperty("type").GetString().ShouldBe("Reversal");
        reversal.GetProperty("amount").GetDecimal().ShouldBe(-100m);
        reversal.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        reversal.GetProperty("originalTransactionId").GetGuid().ShouldBe(originalId);
        (await CountTransactionsAsync(accountId)).ShouldBe(2);
    }

    [Test]
    public async Task Reverse_RequiresAKeyAndReplaysTheSameRequest()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var first = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 40m));
        var second = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 15m, "2026-09-24T10:00:00+12:00"));
        var key = Guid.NewGuid();

        (await ReverseAsync(tokens.AccessToken, first, Guid.Empty)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var created = await ReverseAsync(tokens.AccessToken, first, key);
        var reversalId = await ReadId(created);
        var replay = await ReverseAsync(tokens.AccessToken, first, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(reversalId);

        (await ReverseAsync(tokens.AccessToken, second, key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CountTransactionsAsync(accountId)).ShouldBe(3);
    }

    [Test]
    public async Task Reverse_RejectsASecondReversalAndAReversalOfAReversal()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var originalId = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 20m));
        var reversalId = await ReadId(await ReverseAsync(tokens.AccessToken, originalId));

        (await ReverseAsync(tokens.AccessToken, originalId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReverseAsync(tokens.AccessToken, reversalId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Reverse_RequiresAnOpenAccount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var depositId = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 30m));
        var withdrawalId = await ReadId(await PostAsync(
            tokens.AccessToken, accountId, "TransferOut", -30m, "2026-09-24T10:00:00+12:00"));
        (await CloseAsync(accountId, tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReverseAsync(tokens.AccessToken, withdrawalId)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var rowVersion = await ReadAccountRowVersion(accountId, tokens.AccessToken);
        (await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/reopen", tokens.AccessToken, new { rowVersion }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reversal = await ReverseAsync(tokens.AccessToken, withdrawalId);
        reversal.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = await ReadItem(tokens.AccessToken, await ReadId(reversal));
        item.GetProperty("amount").GetDecimal().ShouldBe(30m);
        item.GetProperty("originalTransactionId").GetGuid().ShouldBe(withdrawalId);
        _ = depositId;
    }

    [Test]
    public async Task Reverse_DisabledCustomerReturns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var originalId = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 10m));
        await DisableCustomerAsync(customer.Id);

        var response = await ReverseAsync(tokens.AccessToken, originalId);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task Reverse_OtherTenantIs404AndSystemAdminMayCrossTenants()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example");
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var originalId = await ReadId(await PostAsync(tokens.AccessToken, accountId, "TransferIn", 12m));

        var (_, _, _, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example");
        (await ReverseAsync(southTokens.AccessToken, originalId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await AccountHttp.SignInSystemAdmin();
        (await ReverseAsync(system.AccessToken, originalId)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Guid accountId,
        string type,
        decimal amount,
        string bookedAt = "2026-09-24T09:00:00+12:00") =>
        await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type,
            amount,
            bookedAt
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id, Guid? key = null)
    {
        Guid? idempotencyKey = key == Guid.Empty ? (Guid?)null : key ?? Guid.NewGuid();
        return TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, body: null, idempotencyKey);
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadItem(string accessToken, Guid id)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", accessToken);
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

    private static async Task<HttpResponseMessage> CloseAsync(Guid accountId, string accessToken)
    {
        var rowVersion = await ReadAccountRowVersion(accountId, accessToken);
        return await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }

    private static async Task<string?> ReadAccountRowVersion(Guid accountId, string accessToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("rowVersion").GetString();
    }

    private static async Task<int> CountTransactionsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountId);
        return await db.Transactions.CountAsync(row => row.AccountId == account.Id);
    }

    private static async Task DisableCustomerAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == customerId);
        person.Disable();
        await db.SaveChangesAsync();
    }
}
