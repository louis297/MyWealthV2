using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

/// <summary>One tenant with a TenantAdmin, an Adviser and a Customer, and an Open Custodial USD account with its
/// deposit reference and the tenant's USD client money account (deposit-matching §10).</summary>
internal sealed record DepositMatchingWorld(
    Tenant Tenant,
    User Adviser,
    User Customer,
    TokenResponse TenantAdmin,
    Guid AccountId,
    string DepositReference,
    ClientMoneyAccount ClientMoney)
{
    public static async Task<DepositMatchingWorld> CreateAsync(
        string name = "North Advisory",
        string code = "north-advisory",
        string currency = "USD")
    {
        var (tenant, adviser, customer, tokens) = await AccountHttp.SignInTenantAdmin(
            name, code, $"tess@{code}.example", adviserEmail: $"ann@{code}.example", customerEmail: $"ned@{code}.example");
        var accountId = await CustodialHttp.OpenCustodialAsync(tokens.AccessToken, customer.PublicId, currency: currency);
        var reference = await BankFeedHttp.DepositReferenceAsync(accountId);
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(tenant.Id, currency);
        return new DepositMatchingWorld(tenant, adviser, customer, tokens, accountId, reference, clientMoney);
    }

    public Task<TokenResponse> SignInAdviserAsync() =>
        FunctionalTestSetup.Oidc.SignInAsync(Adviser.Email, "Password1!", Tenant.Code);

    /// <summary>Adds a receipt to this tenant's client money account and runs the job once (R13).</summary>
    public async Task<BankStatementLine> ReceiveAsync(decimal amount, string? reference, string? currency = null)
    {
        var sent = BankFeedHttp.Feed.AddReceipt(ClientMoney.AccountNumber, amount, currency ?? ClientMoney.Currency, reference);
        await BankFeedHttp.RunAsync();
        return await BankFeedHttp.LineAsync(sent.Id!);
    }

    /// <summary>The TenantAdmin person who signed in (<c>tess@{code}.example</c>).</summary>
    public async Task<User> TenantAdminUserAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = $"tess@{Tenant.Code}.example";
        return await db.DomainUsers.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.TenantId == Tenant.Id && row.Email == email);
    }

    public async Task<decimal> CashBalanceAsync() => await CustodialHttp.CashBalanceAsync(TenantAdmin.AccessToken, AccountId);
}
