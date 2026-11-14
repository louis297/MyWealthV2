using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Internal transfers R1–R10, R13: POST /transactions/transfer create, idempotency and validation.</summary>
public class InternalTransferTests : TestBase
{
    private const string TransferPath = "/transactions/transfer";
    private const string BookedAt = "2026-11-10T09:00:00Z";

    [Test]
    public async Task NoBearer_Returns401()
    {
        var response = await TransactionHttp.Send(
            HttpMethod.Post, TransferPath, accessToken: null, Body(Guid.NewGuid(), Guid.NewGuid(), 10m), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var nedTokens = await TestApp.IssueAccessTokenAsync(world.Ned);

        (await PostAsync(nedTokens.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_SameCustomer_CreatesTwoLegsAndMovesTheCash()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var savings = await OpenAccountAsync(world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned savings");
        await FundAsync(world, world.NedAccountId, 1000m);

        var response = await PostAsync(
            world.TenantAdmin.AccessToken, Body(world.NedAccountId, savings, 500m, memo: "Move to savings"));

        var id = await LegAccountsFixture.ReadIdAsync(response);
        response.Headers.Location?.OriginalString.ShouldBe($"/transactions/{id}");
        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NedAccountId, -500m),
            (savings, 500m)
        ]);
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(500m);
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, savings)).ShouldBe(500m);

        var get = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{id}", world.TenantAdmin.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("type").GetString().ShouldBe("InternalTransfer");
        json.RootElement.GetProperty("memo").GetString().ShouldBe("Move to savings");
        TransactionJson.CashLegs(json.RootElement).Count.ShouldBe(2);
        TransactionJson.SecurityLegs(json.RootElement).ShouldBeEmpty();
    }

    [Test]
    public async Task TenantAdmin_TwoCustomers_MovesTheCash()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(
            world.TenantAdmin.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 40m)));

        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NedAccountId, -40m),
            (world.NoraAccountId, 40m)
        ]);
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(60m);
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, world.NoraAccountId)).ShouldBe(40m);
    }

    [Test]
    public async Task Adviser_AssignedToBothCustomers_MovesTheCash()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var nell = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Nell North", "nell@north.example", "Password1!", world.Tenant.Id, world.Ned.AdviserId);
        var nellAccount = await OpenAccountAsync(world.TenantAdmin.AccessToken, nell.PublicId, "Nell bank");
        await FundAsync(world, world.NedAccountId, 100m);

        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(
            world.Ann.AccessToken, Body(world.NedAccountId, nellAccount, 25m)));

        (await LegAccountsFixture.CashLegsAsync(id)).OrderBy(leg => leg.Amount).ShouldBe([
            (world.NedAccountId, -25m),
            (nellAccount, 25m)
        ]);
    }

    [Test]
    public async Task SameAccount_Returns400()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);

        await ShouldRejectAsync(world, Body(world.NedAccountId, world.NedAccountId, 10m));
    }

    [Test]
    public async Task CurrencyMismatch_Returns400()
    {
        var world = await LegAccountsFixture.CreateAsync();
        var aud = await OpenAccountAsync(world.TenantAdmin.AccessToken, world.Ned.PublicId, "Ned AUD", "AUD");
        await FundAsync(world, world.NedAccountId, 100m);

        await ShouldRejectAsync(world, Body(world.NedAccountId, aud, 10m));
    }

    [TestCase(0)]
    [TestCase(-10)]
    public async Task AmountNotPositive_Returns400(decimal amount)
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);

        await ShouldRejectAsync(world, Body(world.NedAccountId, world.NoraAccountId, amount));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClosedFromOrToAccount_Returns400(bool closeFrom)
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        await LegAccountsFixture.CloseAccountDirectlyAsync(closeFrom ? world.NedAccountId : world.NoraAccountId);

        await ShouldRejectAsync(world, Body(world.NedAccountId, world.NoraAccountId, 10m));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DisabledCustomerOnEitherSide_Returns400TargetUser(bool disableFrom)
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        await LegAccountsFixture.DisableCustomerAsync(disableFrom ? world.Ned.Id : world.Nora.Id);
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        var response = await PostAsync(
            world.TenantAdmin.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 10m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    [Test]
    public async Task DisabledTenant_TenantUser_Returns403()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        await DisableTenantAsync(world.Tenant.Id);

        var response = await PostAsync(
            world.TenantAdmin.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 10m));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DisabledTenant_SystemAdmin_Returns400TargetTenant()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        await DisableTenantAsync(world.Tenant.Id);
        var system = await AccountHttp.SignInSystemAdmin();
        var body = Body(world.NedAccountId, world.NoraAccountId, 10m);
        body["tenantId"] = world.Tenant.PublicId;

        var response = await PostAsync(system.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
    }

    [Test]
    public async Task InsufficientCash_Returns400AndWritesNothing()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);

        await ShouldRejectAsync(world, Body(world.NedAccountId, world.NoraAccountId, 100.01m));
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(100m);
    }

    [TestCase("type", "InternalTransfer")]
    [TestCase("accountId", "8f0c9a52-1d0e-4bd1-9a7a-3b0f8b3b1b11")]
    [TestCase("currency", "NZD")]
    public async Task ForbiddenBodyField_Returns400(string field, string value)
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        var body = Body(world.NedAccountId, world.NoraAccountId, 10m);
        body[field] = value;

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task CashLegsArrayInBody_Returns400()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        var body = Body(world.NedAccountId, world.NoraAccountId, 10m);
        body["cash"] = new { amount = 10m, currency = "NZD" };

        await ShouldRejectAsync(world, body);
    }

    [Test]
    public async Task IdempotencyKey_IsRequiredReplaysAndRejectsADifferentBody()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        var body = Body(world.NedAccountId, world.NoraAccountId, 10m);

        (await TransactionHttp.Send(HttpMethod.Post, TransferPath, world.TenantAdmin.AccessToken, body, idempotencyKey: null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid();
        var id = await LegAccountsFixture.ReadIdAsync(await PostAsync(world.TenantAdmin.AccessToken, body, key));
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        var replay = await PostAsync(world.TenantAdmin.AccessToken, body, key);
        (await LegAccountsFixture.ReadIdAsync(replay)).ShouldBe(id);

        (await PostAsync(world.TenantAdmin.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 11m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
        (await CashBalanceAsync(world.TenantAdmin.AccessToken, world.NedAccountId)).ShouldBe(90m);
    }

    [Test]
    public async Task IdempotencyKey_UsedOnAnotherRouteIsAConflict()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);
        var key = Guid.NewGuid();
        (await TransactionHttp.Send(HttpMethod.Post, "/transactions", world.TenantAdmin.AccessToken, new
            {
                accountId = world.NedAccountId,
                type = "TransferIn",
                amount = 10m,
                bookedAt = BookedAt
            }, key))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(world.TenantAdmin.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 10m), key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task TenantAdminOmitsTenantId_SystemAdminMustSendIt()
    {
        var world = await LegAccountsFixture.CreateAsync();
        await FundAsync(world, world.NedAccountId, 100m);

        var withTenant = Body(world.NedAccountId, world.NoraAccountId, 10m);
        withTenant["tenantId"] = world.Tenant.PublicId;
        (await PostAsync(world.TenantAdmin.AccessToken, withTenant))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var system = await AccountHttp.SignInSystemAdmin();
        (await PostAsync(system.AccessToken, Body(world.NedAccountId, world.NoraAccountId, 10m)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(system.AccessToken, withTenant))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    internal static Dictionary<string, object?> Body(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        string? memo = null) =>
        new()
        {
            ["fromAccountId"] = fromAccountId,
            ["toAccountId"] = toAccountId,
            ["amount"] = amount,
            ["bookedAt"] = BookedAt,
            ["memo"] = memo,
            ["reference"] = null
        };

    internal static Task<HttpResponseMessage> PostAsync(string accessToken, object body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, TransferPath, accessToken, body, key ?? Guid.NewGuid());

    internal static Task FundAsync(LegAccountsWorld world, Guid accountId, decimal amount) =>
        LegAccountsFixture.InsertCashTransactionAsync(world.Tenant.Id, TransactionType.TransferIn, (accountId, amount));

    internal static async Task<decimal> CashBalanceAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("cashBalance").GetDecimal();
    }

    internal static async Task<Guid> OpenAccountAsync(
        string accessToken,
        Guid customerId,
        string name,
        string currency = "NZD",
        string type = "Bank")
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task ShouldRejectAsync(LegAccountsWorld world, object body)
    {
        var before = await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id);

        (await PostAsync(world.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LegAccountsFixture.CountTransactionsAsync(world.Tenant.Id)).ShouldBe(before);
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
