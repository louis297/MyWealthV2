using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class CreateTransactionTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var post = await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken: null, new
        {
            accountId = Guid.NewGuid(),
            type = "TransferIn",
            amount = 100.00m,
            bookedAt = "2026-09-24T09:00:00Z"
        });
        var get = await TransactionHttp.Send(HttpMethod.Get, "/transactions", accessToken: null);

        post.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        post.Headers.Location.ShouldBeNull();
        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        get.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);
        var body = new
        {
            accountId = Guid.NewGuid(),
            type = "TransferIn",
            amount = 100.00m,
            bookedAt = "2026-09-24T09:00:00Z"
        };

        var post = await TransactionHttp.Send(
            HttpMethod.Post, "/transactions", tokens.AccessToken, body, Guid.NewGuid());

        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Customer_ReadsOwnTransactions_OtherCustomerIs404()
    {
        var (tenant, adviser, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var ownAccount = await OpenAccountAsync(adviserTokens.AccessToken, customer.PublicId);
        var ownId = await PostTransferInAsync(adviserTokens.AccessToken, ownAccount);

        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var otherAccount = await OpenAccountAsync(adviserTokens.AccessToken, otherCustomer.PublicId);
        var otherId = await PostTransferInAsync(adviserTokens.AccessToken, otherAccount);

        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await OpenAccountAsync(southTokens.AccessToken, southCustomer.PublicId);
        var southId = await PostTransferInAsync(southTokens.AccessToken, southAccount);

        var tokens = await TestApp.IssueAccessTokenAsync(customer);
        var list = await TransactionHttp.Send(HttpMethod.Get, "/transactions", tokens.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        listJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([ownId]);

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{ownId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions?accountId={ownAccount}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{otherId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions?accountId={otherAccount}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionHttp.Send(
                HttpMethod.Get, $"/transactions?customerId={otherCustomer.PublicId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{southId}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions?accountId={southAccount}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> PostTransferInAsync(string accessToken, Guid accountId)
    {
        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 25.00m,
            bookedAt = "2026-09-24T09:00:00Z"
        }, Guid.NewGuid());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Test]
    public async Task Adviser_PostsTransferInAndGetMatches()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        const string bookedAt = "2026-09-24T09:00:00+12:00";

        var response = await TransactionHttp.Send(HttpMethod.Post, "/transactions", tokens.AccessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 100.00m,
            bookedAt,
            memo = "  Salary  ",
            reference = (string?)null
        }, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", tokens.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var item = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        item.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        item.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(tenant.PublicId);
        item.RootElement.TryGetProperty("accountId", out _).ShouldBeFalse();
        item.RootElement.TryGetProperty("customerId", out _).ShouldBeFalse();
        TransactionJson.SingleCash(item.RootElement).GetProperty("accountId").GetGuid().ShouldBe(accountId);
        TransactionJson.SingleCash(item.RootElement).GetProperty("customerId").GetGuid().ShouldBe(customer.PublicId);
        item.RootElement.GetProperty("type").GetString().ShouldBe("TransferIn");
        item.RootElement.TryGetProperty("amount", out _).ShouldBeFalse();
        TransactionJson.SingleCash(item.RootElement).GetProperty("amount").GetDecimal().ShouldBe(100.00m);
        TransactionJson.SingleCash(item.RootElement).GetProperty("currency").GetString().ShouldBe("NZD");
        TransactionJson.SecurityLegs(item.RootElement).ShouldBeEmpty();
        item.RootElement.GetProperty("bookedAt").GetString().ShouldBe(bookedAt);
        item.RootElement.GetProperty("memo").GetString().ShouldBe("Salary");
        item.RootElement.GetProperty("reference").ValueKind.ShouldBe(JsonValueKind.Null);
        item.RootElement.GetProperty("originalTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.RootElement.GetProperty("reversedByTransactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.RootElement.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
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
