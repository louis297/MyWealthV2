using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GrantTenantReportTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{Guid.NewGuid()}/reports",
            accessToken: null,
            body: new { reportId = CustomerTransactionsId });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task OtherRoles_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Grant Post", "firm-grant-post");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-grant-post", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-grant-post", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-grant-post", "Password1!", tenant.Id, adviser.Id);
        var body = new { reportId = CustomerTransactionsId };

        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-grant-post", "Password1!", "firm-grant-post");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-grant-post", "Password1!", "firm-grant-post");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenant.PublicId}/reports", admin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenant.PublicId}/reports", adviserTokens.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenant.PublicId}/reports", customerTokens.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UnknownTenantOrReport_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var missingTenant = Guid.NewGuid();
        var missingReport = Guid.NewGuid();
        var tenant = await TestApp.CreateTenantAsync("Known Grant", "known-grant");

        var tenantResponse = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{missingTenant}/reports",
            tokens.AccessToken,
            new { reportId = CustomerTransactionsId });
        tenantResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantResponse.Content.ReadAsStringAsync()).ShouldContain(missingTenant.ToString());

        var reportResponse = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenant.PublicId}/reports",
            tokens.AccessToken,
            new { reportId = missingReport });
        reportResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reportResponse.Content.ReadAsStringAsync()).ShouldContain(missingReport.ToString());
    }

    [Test]
    public async Task FirstGrant_Returns201_RepeatReturns204_InactiveCatalogRowCanBeGranted()
    {
        var tenant = await TestApp.CreateTenantAsync("Grant Once", "grant-once");
        var tokens = await TenantHttp.SignInSystemAdmin();
        await SetCatalogActiveAsync(CustomerTransactionsId, active: false);

        try
        {
            var created = await TenantHttp.Send(
                HttpMethod.Post,
                $"/tenants/{tenant.PublicId}/reports",
                tokens.AccessToken,
                new { reportId = CustomerTransactionsId });
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            using (var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            {
                json.RootElement.GetProperty("id").GetGuid().ShouldBe(CustomerTransactionsId);
            }

            var again = await TenantHttp.Send(
                HttpMethod.Post,
                $"/tenants/{tenant.PublicId}/reports",
                tokens.AccessToken,
                new { reportId = CustomerTransactionsId });
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var list = await TenantHttp.Send(
                HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
            using var granted = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var items = granted.RootElement.EnumerateArray().ToList();
            items.Select(item => item.GetProperty("code").GetString())
                .ShouldBe(["adviser-balances"]);
            items.Single().GetProperty("isActive").GetBoolean().ShouldBeTrue();
            items.Single().GetProperty("granted").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await SetCatalogActiveAsync(CustomerTransactionsId, active: true);
        }
    }

    private static async Task SetCatalogActiveAsync(Guid reportPublicId, bool active)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active} WHERE [PublicId] = {reportPublicId}");
    }
}
