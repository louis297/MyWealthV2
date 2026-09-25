using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class TransactionRulesTests : TestBase
{
    [Test]
    public async Task TransferOutAndInterest_UpdateTheCashSum()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);

        (await PostAsync(tokens.AccessToken, accountId, "TransferIn", 100m, "2026-09-24T09:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, accountId, "Interest", -2.50m, "2026-09-24T10:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, accountId, "TransferOut", -40m, "2026-09-24T11:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var overdraft = await PostAsync(tokens.AccessToken, accountId, "TransferOut", -80m, "2026-09-24T12:00:00+12:00");
        overdraft.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountTransactionsAsync(accountId)).ShouldBe(3);
    }

    [Test]
    public async Task CurrencyDifferentFromTheAccount_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);

        var response = await PostAsync(
            tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00", currency: "USD");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountTransactionsAsync(accountId)).ShouldBe(0);
    }

    [TestCase("Buy")]
    [TestCase("Sell")]
    [TestCase("Dividend")]
    [TestCase("Reversal")]
    public async Task TypeOutsideTheIncrement_Returns400(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var amount = type == "Buy" ? -10m : 10m;

        var response = await PostAsync(tokens.AccessToken, accountId, type, amount, "2026-09-24T09:00:00+12:00");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ClosedAccount_Returns400()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        await CloseAccountAsync(accountId, tokens.AccessToken);

        var response = await PostAsync(tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DisabledCustomer_Returns400TargetUser()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        await DisableCustomerAsync(customer.Id);

        var response = await PostAsync(tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }

    [Test]
    public async Task DisabledTenant_CreateReturns400AndGetStillWorks()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var created = await PostAsync(tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00");
        var id = await ReadId(created);
        await DisableTenantAsync(tenant.Id);

        var again = await PostAsync(tokens.AccessToken, accountId, "TransferIn", 5m, "2026-09-24T10:00:00+12:00");
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TransactionHttp.Send(HttpMethod.Get, "/transactions", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Opening_IsOnlyTheFirstTransaction()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var firstAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Opening book");
        (await PostAsync(tokens.AccessToken, firstAccount, "Opening", 25m, "2026-09-24T09:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, firstAccount, "Opening", 5m, "2026-09-24T10:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var secondAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Later book");
        (await PostAsync(tokens.AccessToken, secondAccount, "TransferIn", 10m, "2026-09-24T09:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, secondAccount, "Opening", 10m, "2026-09-24T10:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task IdempotencyKey_ReplaysAndRejectsADifferentBody()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(tokens.AccessToken, customer.PublicId);
        var key = Guid.NewGuid();

        var missing = await PostAsync(
            tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00", key: Guid.Empty);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var first = await PostAsync(
            tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00", key: key);
        var id = await ReadId(first);
        var replay = await PostAsync(
            tokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00", key: key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadId(replay)).ShouldBe(id);

        var mismatch = await PostAsync(
            tokens.AccessToken, accountId, "TransferIn", 11m, "2026-09-24T09:00:00+12:00", key: key);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CountTransactionsAsync(accountId)).ShouldBe(1);
    }

    [Test]
    public async Task TenantAdminOmitsTenantId_SystemAdminMustSendIt()
    {
        var (tenant, _, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await OpenAccountAsync(adminTokens.AccessToken, customer.PublicId);

        (await PostAsync(adminTokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(
                adminTokens.AccessToken, accountId, "TransferIn", 5m, "2026-09-24T10:00:00+12:00", tenantId: tenant.PublicId))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var system = await AccountHttp.SignInSystemAdmin();
        (await PostAsync(system.AccessToken, accountId, "TransferIn", 5m, "2026-09-24T11:00:00+12:00"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(
                system.AccessToken, accountId, "TransferIn", 5m, "2026-09-24T11:00:00+12:00", tenantId: Guid.NewGuid()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(
                system.AccessToken, accountId, "TransferIn", 5m, "2026-09-24T11:00:00+12:00", tenantId: tenant.PublicId))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task OtherTenantAndUnassignedCustomer_Return404()
    {
        var (north, _, customer, adviserTokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenAccountAsync(adviserTokens.AccessToken, customer.PublicId);
        var created = await PostAsync(adviserTokens.AccessToken, accountId, "TransferIn", 10m, "2026-09-24T09:00:00+12:00");
        var id = await ReadId(created);

        var (_, _, _, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example");
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", southTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var system = await AccountHttp.SignInSystemAdmin();
        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", north.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", north.Id, otherAdviser.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north.example", "Password1!", north.Id);
        var tenantAdmin = await FunctionalTestSetup.Oidc.SignInAsync("tess@north.example", "Password1!", "north-advisory");
        var otherAccount = await OpenAccountAsync(tenantAdmin.AccessToken, otherCustomer.PublicId, "Pat bank");
        var otherCreated = await PostAsync(
            tenantAdmin.AccessToken, otherAccount, "TransferIn", 8m, "2026-09-24T08:00:00+12:00");
        var otherId = await ReadId(otherCreated);

        (await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{otherId}", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await TransactionHttp.Send(HttpMethod.Get, "/transactions", adviserTokens.AccessToken);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([id]);
    }

    [Test]
    public async Task List_FiltersAndSortsByBookedAtThenId()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var firstAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "First");
        var secondAccount = await OpenAccountAsync(tokens.AccessToken, customer.PublicId, "Second");
        var later = await ReadId(await PostAsync(
            tokens.AccessToken, firstAccount, "TransferIn", 10m, "2026-09-24T11:00:00+12:00"));
        var earlier = await ReadId(await PostAsync(
            tokens.AccessToken, secondAccount, "TransferIn", 3m, "2026-09-24T09:00:00+12:00"));

        var list = await TransactionHttp.Send(HttpMethod.Get, "/transactions", tokens.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(20);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([earlier, later]);

        var filtered = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={secondAccount}&customerId={customer.PublicId}", tokens.AccessToken);
        using var filteredJson = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        filteredJson.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([earlier]);

        (await TransactionHttp.Send(HttpMethod.Get, "/transactions?pageSize=101", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Guid accountId,
        string type,
        decimal amount,
        string bookedAt,
        Guid? key = null,
        Guid? tenantId = null,
        string? currency = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["type"] = type,
            ["amount"] = amount,
            ["bookedAt"] = bookedAt
        };
        if (tenantId is not null)
        {
            body["tenantId"] = tenantId;
        }

        if (currency is not null)
        {
            body["currency"] = currency;
        }

        Guid? idempotencyKey = key == Guid.Empty ? (Guid?)null : key ?? Guid.NewGuid();
        return await TransactionHttp.Send(
            HttpMethod.Post, "/transactions", accessToken, body, idempotencyKey);
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string name = "Everyday spending")
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task CloseAccountAsync(Guid accountId, string accessToken)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        var close = await AccountHttp.Send(
            HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<int> CountTransactionsAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountId);
        return await db.Transactions.CountAsync(row => row.AccountId == account.Id);
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
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
