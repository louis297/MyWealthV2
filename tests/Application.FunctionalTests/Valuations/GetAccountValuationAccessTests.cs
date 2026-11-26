using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Account valuation R1: who reads the valuation of which Account; any other Account is 404.</summary>
[Parallelizable]
public class GetAccountValuationAccessTests : TestBase
{
    private const string Date = "2026-03-05";

    [Test]
    public async Task TheCustomer_ReadsTheirOwnAccount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await FundedAccountAsync(tokens.AccessToken, customer.PublicId);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await ReadAsync(customerTokens.AccessToken, accountId, new DateOnly(2026, 3, 5))).GetProperty("cash").GetDecimal()
            .ShouldBe(7.00m);
    }

    [Test]
    public async Task TheAdviser_ReadsAnAssignedCustomersAccount()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await FundedAccountAsync(tokens.AccessToken, customer.PublicId);

        (await GetAsync(tokens.AccessToken, accountId, Date)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ASystemAdmin_ReadsAnAccountOfAnyTenant()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await FundedAccountAsync(tokens.AccessToken, customer.PublicId);
        var admin = await AccountHttp.SignInSystemAdmin();

        (await ReadAsync(admin.AccessToken, accountId, new DateOnly(2026, 3, 5))).GetProperty("cash").GetDecimal()
            .ShouldBe(7.00m);
    }

    [Test]
    public async Task EveryRoleOfTenantA_WithAnAccountOfTenantB_Gets404()
    {
        var (_, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var (_, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await FundedAccountAsync(southTokens.AccessToken, southCustomer.PublicId);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { adminTokens.AccessToken, adviserTokens.AccessToken, customerTokens.AccessToken })
        {
            (await GetAsync(token, southAccount, Date)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await GetAsync(adminTokens.AccessToken, Guid.NewGuid(), Date)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnAdviser_WithAnUnassignedCustomersAccount_Gets404()
    {
        var (tenant, adviser, _, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        var unassigned = await FundedAccountAsync(adminTokens.AccessToken, otherCustomer.PublicId);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);

        (await GetAsync(adviserTokens.AccessToken, unassigned, Date)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ACustomer_WithAnotherCustomersAccount_Gets404()
    {
        var (tenant, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var theirs = await FundedAccountAsync(adminTokens.AccessToken, otherCustomer.PublicId);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await GetAsync(customerTokens.AccessToken, theirs, Date)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> FundedAccountAsync(string accessToken, Guid customerId)
    {
        var accountId = await AccountHttp.OpenAccountAsync(accessToken, customerId, "Held elsewhere", "Brokerage", "NZD");
        await PostAsync(accessToken, Cash(accountId, "TransferIn", 7.00m, "2026-03-02T09:00:00Z"));
        return accountId;
    }
}
