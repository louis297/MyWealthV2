using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Valuation-run R1, R19: who reads the stored valuations of which Account; any other Account is 404.</summary>
[Parallelizable]
public class StoredValuationAccessTests : TestBase
{
    private const string Date = "2026-03-03";

    private static IEnumerable<string> Routes(Guid accountId) =>
        [$"/accounts/{accountId}/valuations", $"/accounts/{accountId}/valuations/{Date}"];

    [Test]
    public async Task TheCustomer_ReadsTheirOwnAccount_WithEveryField()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        var accountId = await StoredAccountAsync(tenant.Id, tokens.AccessToken, customer.PublicId);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        var item = await ValuationRunHttp.GetAsync(customerTokens.AccessToken, $"/accounts/{accountId}/valuations/{Date}");
        item.GetProperty("cash").GetDecimal().ShouldBe(7.00m);
        item.TryGetProperty("runId", out _).ShouldBeTrue();
        item.TryGetProperty("valuedAt", out _).ShouldBeTrue();
        (await ValuationRunHttp.GetAsync(customerTokens.AccessToken, $"/accounts/{accountId}/valuations"))
            .GetProperty("totalCount").GetInt32().ShouldBe(2);
    }

    [Test]
    public async Task TheAdviser_ReadsAnAssignedCustomersAccount_AndASystemAdminAnyTenants()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await StoredAccountAsync(tenant.Id, tokens.AccessToken, customer.PublicId);
        var admin = await AccountHttp.SignInSystemAdmin();

        foreach (var route in Routes(accountId))
        {
            (await ValuationRunHttp.Send(HttpMethod.Get, route, tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await ValuationRunHttp.Send(HttpMethod.Get, route, admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task EveryRoleOfTenantA_WithAnAccountOfTenantB_Gets404()
    {
        var (_, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var (south, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var southAccount = await StoredAccountAsync(south.Id, southTokens.AccessToken, southCustomer.PublicId);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { adminTokens.AccessToken, adviserTokens.AccessToken, customerTokens.AccessToken })
        {
            foreach (var route in Routes(southAccount))
            {
                (await ValuationRunHttp.Send(HttpMethod.Get, route, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }
        }

        foreach (var route in Routes(Guid.NewGuid()))
        {
            (await ValuationRunHttp.Send(HttpMethod.Get, route, adminTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task AnAdviser_WithAnUnassignedCustomersAccount_Gets404()
    {
        var (tenant, adviser, _, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        var unassigned = await StoredAccountAsync(tenant.Id, adminTokens.AccessToken, otherCustomer.PublicId);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);

        foreach (var route in Routes(unassigned))
        {
            (await ValuationRunHttp.Send(HttpMethod.Get, route, adviserTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task ACustomer_WithAnotherCustomersAccount_Gets404()
    {
        var (tenant, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, adviser.Id);
        var theirs = await StoredAccountAsync(tenant.Id, adminTokens.AccessToken, otherCustomer.PublicId);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var route in Routes(theirs))
        {
            (await ValuationRunHttp.Send(HttpMethod.Get, route, customerTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task WithoutBearer_Returns401()
    {
        foreach (var route in Routes(Guid.NewGuid()))
        {
            (await ValuationRunHttp.Send(HttpMethod.Get, route, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    /// <summary>An account with 7.00 from Monday 2 March 2026, made Custodial and stored through Tuesday 3 March.</summary>
    private static async Task<Guid> StoredAccountAsync(int tenantId, string accessToken, Guid customerId)
    {
        var accountId = await AccountHttp.OpenAccountAsync(accessToken, customerId, "Managed", "Brokerage", "NZD");
        await PostAsync(accessToken, Cash(accountId, "TransferIn", 7.00m, "2026-03-02T09:00:00Z"));
        await ValuationRunDb.MakeCustodialAsync(accountId, new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero));
        await ValuationRunDb.RunAsync(tenantId, new DateOnly(2026, 3, 3));
        return accountId;
    }
}
