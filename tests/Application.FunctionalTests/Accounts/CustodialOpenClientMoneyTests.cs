using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>
/// Client money accounts R11–R13: <c>POST /accounts</c> opens a Custodial account only in a currency in which the
/// account's Tenant has an active client money account. These tests post their own bodies, so no helper adds
/// a client money account behind their back.
/// </summary>
[Parallelizable]
public class CustodialOpenClientMoneyTests : TestBase
{
    private static readonly CustodyMode[] ExternalOnly = [CustodyMode.External];

    [Test]
    public async Task TenantAdmin_OpensCustodialOnlyInACoveredCurrency()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        await ClientMoneyHttp.EnsureActiveAsync(tenant.Id, "USD");

        await AssertCoveredCurrencyOnlyAsync(tokens.AccessToken, customer.PublicId, customer.Id);
    }

    [Test]
    public async Task Adviser_OpensCustodialOnlyInACoveredCurrency()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        await ClientMoneyHttp.EnsureActiveAsync(tenant.Id, "USD");

        await AssertCoveredCurrencyOnlyAsync(tokens.AccessToken, customer.PublicId, customer.Id);
    }

    [Test]
    public async Task TenantWithoutClientMoneyAccounts_OpensExternalInAnyCurrency()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();

        var external = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "External", "Bank", "USD"));

        external.StatusCode.ShouldBe(HttpStatusCode.Created, await external.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task TenantNotAllowingCustodial_AndWithoutClientMoneyAccount_GetsTheCustodyModeMessage()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser(allowedCustodyModes: ExternalOnly);

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", "Brokerage", "USD"));

        var errors = await ErrorsAsync(response);
        errors["CustodyMode"].ShouldBe(["The tenant does not allow Custodial accounts."]);
        errors.Keys.ShouldNotContain("Currency");
    }

    [Test]
    public async Task SystemAdmin_FollowsTheTargetTenantsClientMoneyAccounts()
    {
        var (north, _, northCustomer, _) = await AccountHttp.SignInAdviser();
        var (south, _, _, _) = await AccountHttp.SignInAdviser(
            "South Advisory", "south-advisory", "sam@south.example", customerEmail: "sue@south.example");
        await ClientMoneyHttp.EnsureActiveAsync(north.Id, "USD");
        await ClientMoneyHttp.EnsureActiveAsync(south.Id, "NZD");
        var tokens = await AccountHttp.SignInSystemAdmin();

        var nzd = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            SystemAdminBody(north.PublicId, northCustomer.PublicId, "NZD"));
        (await ErrorsAsync(nzd))["Currency"].ShouldBe([NoClientMoneyMessage("NZD")]);

        var usd = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            SystemAdminBody(north.PublicId, northCustomer.PublicId, "USD"));
        usd.StatusCode.ShouldBe(HttpStatusCode.Created, await usd.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task DisablingTheLastClientMoneyAccount_LeavesTheExistingAccountWorking_AndRefusesANewOpen()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var system = await AccountHttp.SignInSystemAdmin();
        var row = await ClientMoneyHttp.CreateAsync(system.AccessToken, tenant.PublicId, ClientMoneyHttp.Body("USD"));
        var account = await CreatedIdAsync(await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", "Brokerage", "USD")));

        // Funded through the bank feed while the client money account is still read (deposit-matching R5).
        (await FundAsync(tokens.AccessToken, account, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // A Custodial account is emptied by a withdrawal, which pays out of the active client money account
        // (withdrawals R29, R45, R46).
        var withdrawal = await WithdrawAsync(account, 100m);
        withdrawal.StatusCode.ShouldBe(HttpStatusCode.Created, await withdrawal.Content.ReadAsStringAsync());

        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, tenant.PublicId, row, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CloseAsync(tokens.AccessToken, account)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReopenAsync(tokens.AccessToken, account)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await AccountHttp.Send(HttpMethod.Post, "/accounts", tokens.AccessToken,
            Body(customer.PublicId, "Custodial", "Brokerage", "USD"));
        (await ErrorsAsync(again))["Currency"].ShouldBe([NoClientMoneyMessage("USD")]);
        (await CountAccountsAsync(customer.Id, CustodyMode.Custodial)).ShouldBe(1);
    }

    [Test]
    public async Task TwoTenants_AnAdviserNamingAnotherTenantsCustomer_Gets404Not400()
    {
        var (_, _, _, northTokens) = await AccountHttp.SignInAdviser();
        var (south, _, southCustomer, _) = await AccountHttp.SignInAdviser(
            "South Advisory", "south-advisory", "sam@south.example", customerEmail: "sue@south.example");
        await ClientMoneyHttp.EnsureActiveAsync(south.Id, "USD");

        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", northTokens.AccessToken,
            Body(southCustomer.PublicId, "Custodial", "Brokerage", "USD"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountAccountsAsync(southCustomer.Id, CustodyMode.Custodial)).ShouldBe(0);
    }

    private static async Task AssertCoveredCurrencyOnlyAsync(string accessToken, Guid customerPublicId, int customerId)
    {
        var usd = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken,
            Body(customerPublicId, "Custodial", "Brokerage", "USD"));
        usd.StatusCode.ShouldBe(HttpStatusCode.Created, await usd.Content.ReadAsStringAsync());

        var nzd = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken,
            Body(customerPublicId, "Custodial", "Brokerage", "NZD"));
        (await ErrorsAsync(nzd))["Currency"].ShouldBe([NoClientMoneyMessage("NZD")]);
        (await CountAccountsAsync(customerId, CustodyMode.Custodial)).ShouldBe(1);

        var external = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken,
            Body(customerPublicId, "External", "Bank", "NZD"));
        external.StatusCode.ShouldBe(HttpStatusCode.Created, await external.Content.ReadAsStringAsync());
    }

    private static string NoClientMoneyMessage(string currency) =>
        $"The tenant has no active client money account in {currency}, so a Custodial account cannot be opened in {currency}.";

    private static object Body(Guid customerId, string custodyMode, string type, string currency) => new
    {
        customerId,
        name = "New account",
        type,
        custodyMode,
        currency
    };

    private static object SystemAdminBody(Guid tenantId, Guid customerId, string currency) => new
    {
        tenantId,
        customerId,
        name = "New account",
        type = "Brokerage",
        custodyMode = "Custodial",
        currency
    };

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> ReopenAsync(string accessToken, Guid accountId)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/reopen", accessToken, new { rowVersion });
    }

    private static async Task<int> CountAccountsAsync(int customerId, CustodyMode custodyMode)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Accounts.CountAsync(row => row.CustomerId == customerId && row.CustodyMode == custodyMode);
    }
}
