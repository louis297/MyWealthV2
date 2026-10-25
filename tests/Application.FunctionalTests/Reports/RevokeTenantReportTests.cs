using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RevokeTenantReportTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{Guid.NewGuid()}/reports/{CustomerTransactionsId}",
            accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task OtherRoles_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Revoke", "firm-revoke");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-revoke", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-revoke", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-revoke", "Password1!", tenant.Id, adviser.Id);
        var path = $"/tenants/{tenant.PublicId}/reports/{CustomerTransactionsId}";

        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-revoke", "Password1!", "firm-revoke");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-revoke", "Password1!", "firm-revoke");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Delete, path, admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Delete, path, adviserTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Delete, path, customerTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UnknownTenantOrReport_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var missingTenant = Guid.NewGuid();
        var missingReport = Guid.NewGuid();
        var tenant = await TestApp.CreateTenantAsync("Known Revoke", "known-revoke");

        var tenantResponse = await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{missingTenant}/reports/{CustomerTransactionsId}",
            tokens.AccessToken);
        tenantResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantResponse.Content.ReadAsStringAsync()).ShouldContain(missingTenant.ToString());

        var reportResponse = await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{tenant.PublicId}/reports/{missingReport}",
            tokens.AccessToken);
        reportResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reportResponse.Content.ReadAsStringAsync()).ShouldContain(missingReport.ToString());
    }

    [Test]
    public async Task Revoke_RemovesTheGrant_AndASecondRevokeReturns204()
    {
        var tenant = await TestApp.CreateTenantAsync("Revoke Twice", "revoke-twice");
        var tokens = await TenantHttp.SignInSystemAdmin();
        var path = $"/tenants/{tenant.PublicId}/reports/{CustomerTransactionsId}";

        (await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenant.PublicId}/reports",
            tokens.AccessToken,
            new { reportId = CustomerTransactionsId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var revoked = await TenantHttp.Send(HttpMethod.Delete, path, tokens.AccessToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
        using (var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var items = json.RootElement.EnumerateArray().ToList();
            items.Select(item => item.GetProperty("code").GetString())
                .ShouldBe(["adviser-balances", "customer-transactions"]);
            var customer = items.Single(item => item.GetProperty("id").GetGuid() == CustomerTransactionsId);
            customer.GetProperty("granted").GetBoolean().ShouldBeFalse();
            customer.TryGetProperty("isGranted", out _).ShouldBeFalse();
        }

        var again = await TenantHttp.Send(HttpMethod.Delete, path, tokens.AccessToken);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
