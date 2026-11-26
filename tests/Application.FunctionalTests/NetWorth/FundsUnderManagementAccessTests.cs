using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R1, R4: who reads the funds under management series, how a SystemAdmin names the Tenant,
/// and whose accounts each scope counts.</summary>
[Parallelizable]
public class FundsUnderManagementAccessTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FundsHttp.Send(null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task ASystemAdmin_MustNameTheTenant_AndAnUnknownOneIs404()
    {
        var (tenant, _, _, _) = await AccountHttp.SignInTenantAdmin();
        var admin = await AccountHttp.SignInSystemAdmin();

        (await CustodialHttp.ErrorsAsync(await FundsHttp.Send(admin.AccessToken))).Keys.ShouldBe(["TenantId"]);
        (await FundsHttp.Send(admin.AccessToken, $"?tenantId={Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var series = await FundsHttp.GetAsync(admin.AccessToken, $"?tenantId={tenant.PublicId}");
        series.GetProperty("reportingCurrency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task EveryOtherRole_SendingTenantId_Gets400()
    {
        var (tenant, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        foreach (var token in new[] { adminTokens.AccessToken, adviserTokens.AccessToken, customerTokens.AccessToken })
        {
            (await CustodialHttp.ErrorsAsync(await FundsHttp.Send(token, $"?tenantId={tenant.PublicId}"))).Keys.ShouldBe(["TenantId"]);
            (await FundsHttp.Send(token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static readonly string Range = FundsHttp.Range(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3));

    [Test]
    public async Task OneFirmsSeries_NeverCountsAnotherFirmsAccounts_AndASystemAdminReadsTheFirmItNames()
    {
        var (north, _, _, northTokens) = await AccountHttp.SignInTenantAdmin();
        var (south, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        await StoredAccountAsync(south.Id, southTokens.AccessToken, southCustomer.PublicId);
        var admin = await AccountHttp.SignInSystemAdmin();

        Accounts(await FundsHttp.GetAsync(northTokens.AccessToken, Range)).ShouldBe([0, 0]);
        Accounts(await FundsHttp.GetAsync(southTokens.AccessToken, Range)).ShouldBe([1, 1]);
        Accounts(await FundsHttp.GetAsync(admin.AccessToken, $"{Range}&tenantId={south.PublicId}")).ShouldBe([1, 1]);
        Accounts(await FundsHttp.GetAsync(admin.AccessToken, $"{Range}&tenantId={north.PublicId}")).ShouldBe([0, 0]);
        var southDay = (await FundsHttp.GetAsync(southTokens.AccessToken, Range)).GetProperty("days")[1];
        southDay.GetProperty("total").GetDecimal().ShouldBe(7.00m);
    }

    [Test]
    public async Task AnAdviser_CountsOnlyTheCustomersAssignedNow_AndACustomerOnlyTheirOwn()
    {
        var (tenant, adviser, customer, adminTokens) = await AccountHttp.SignInTenantAdmin();
        var otherAdviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        await StoredAccountAsync(tenant.Id, adminTokens.AccessToken, customer.PublicId);
        await StoredAccountAsync(tenant.Id, adminTokens.AccessToken, otherCustomer.PublicId);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var otherAdviserTokens = await TestApp.IssueAccessTokenAsync(otherAdviser);

        Accounts(await FundsHttp.GetAsync(adminTokens.AccessToken, Range)).ShouldBe([2, 2]);
        Accounts(await FundsHttp.GetAsync(adviserTokens.AccessToken, Range)).ShouldBe([1, 1]);
        Accounts(await FundsHttp.GetAsync(customerTokens.AccessToken, Range)).ShouldBe([1, 1]);
        Accounts(await FundsHttp.GetAsync(otherAdviserTokens.AccessToken, Range)).ShouldBe([1, 1]);
    }

    private static List<int> Accounts(JsonElement series) =>
        series.GetProperty("days").EnumerateArray().Select(day => day.GetProperty("accounts").GetInt32()).ToList();

    /// <summary>An account with 7.00 NZD from Monday 2 March 2026, made Custodial and stored through Tuesday 3 March.</summary>
    private static async Task StoredAccountAsync(int tenantId, string accessToken, Guid customerId)
    {
        var accountId = await AccountHttp.OpenAccountAsync(accessToken, customerId, "Managed", "Brokerage", "NZD");
        await PostAsync(accessToken, Cash(accountId, "TransferIn", 7.00m, "2026-03-02T09:00:00Z"));
        await ValuationRunDb.MakeCustodialAsync(accountId, new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero));
        await ValuationRunDb.RunAsync(tenantId, new DateOnly(2026, 3, 3));
    }
}
