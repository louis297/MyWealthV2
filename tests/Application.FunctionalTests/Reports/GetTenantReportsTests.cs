using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GetTenantReportsTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{Guid.NewGuid()}/reports", accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task OtherRoles_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Grants", "firm-grants");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-grants", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-grants", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-grants", "Password1!", tenant.Id, adviser.Id);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-grants", "Password1!", "firm-grants");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-grants", "Password1!", "firm-grants");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", customerTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UnknownTenant_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var missing = Guid.NewGuid();

        var response = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{missing}/reports", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain(missing.ToString());
    }

    [Test]
    public async Task List_ReturnsEveryCatalogRow_WithGrantState_IncludingInactive()
    {
        var tenant = await TestApp.CreateTenantAsync("North Grants", "north-grants");
        var tokens = await TenantHttp.SignInSystemAdmin();

        try
        {
            await SetCatalogActiveAsync(CustomerTransactionsId, active: false);

            var before = await TenantHttp.Send(
                HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
            before.StatusCode.ShouldBe(HttpStatusCode.OK);
            using (var json = JsonDocument.Parse(await before.Content.ReadAsStringAsync()))
            {
                var items = json.RootElement.EnumerateArray().ToList();
                items.Select(item => item.GetProperty("code").GetString())
                    .ShouldBe(["adviser-balances", "customer-transactions"]);

                var adviser = items[0];
                adviser.GetProperty("id").GetGuid().ShouldBe(AdviserBalancesId);
                adviser.GetProperty("name").GetString().ShouldBe("Adviser Balances Report");
                adviser.GetProperty("scope").GetString().ShouldBe("Book");
                adviser.GetProperty("isActive").GetBoolean().ShouldBeTrue();
                adviser.GetProperty("isGranted").GetBoolean().ShouldBeFalse();
                adviser.TryGetProperty("tenantId", out _).ShouldBeFalse();

                var customer = items[1];
                customer.GetProperty("id").GetGuid().ShouldBe(CustomerTransactionsId);
                customer.GetProperty("name").GetString().ShouldBe("Customer Transactions Report");
                customer.GetProperty("function").GetString().ShouldBe("Transactions in a date range for one customer.");
                customer.GetProperty("scope").GetString().ShouldBe("Customer");
                customer.GetProperty("isActive").GetBoolean().ShouldBeFalse();
                customer.GetProperty("isGranted").GetBoolean().ShouldBeFalse();
                customer.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
                    .ShouldBe(["manual", "scheduled"]);
            }

            await GrantAsync(tenant.Id, CustomerTransactionsId);

            var after = await TenantHttp.Send(
                HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
            after.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var granted = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
            var grantedCustomer = granted.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == CustomerTransactionsId);
            grantedCustomer.GetProperty("isActive").GetBoolean().ShouldBeFalse();
            grantedCustomer.GetProperty("isGranted").GetBoolean().ShouldBeTrue();
            granted.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == AdviserBalancesId)
                .GetProperty("isGranted").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await SetCatalogActiveAsync(CustomerTransactionsId, active: true);
        }
    }

    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    private static async Task GrantAsync(int tenantId, Guid reportPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [TenantReports] ([TenantId], [ReportId])
            SELECT {tenantId}, [Id] FROM [Reports] WHERE [PublicId] = {reportPublicId}
            """);
    }

    private static async Task SetCatalogActiveAsync(Guid reportPublicId, bool active)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active} WHERE [PublicId] = {reportPublicId}");
    }
}
