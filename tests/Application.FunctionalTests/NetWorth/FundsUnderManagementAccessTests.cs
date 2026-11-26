using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>Valuation-history R1: who reads the funds under management series and how a SystemAdmin names the Tenant.</summary>
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
}
