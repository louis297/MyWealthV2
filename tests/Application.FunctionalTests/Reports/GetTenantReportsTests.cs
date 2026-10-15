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
    public async Task NewTenant_ReturnsEmptyList_AndAGrantIncludesAnInactiveCatalogRow()
    {
        var tenant = await TestApp.CreateTenantAsync("North Grants", "north-grants");
        var tokens = await TenantHttp.SignInSystemAdmin();

        var empty = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()))
        {
            json.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
            json.RootElement.GetArrayLength().ShouldBe(0);
        }

        await GrantAsync(tenant.Id, CustomerTransactionsId);
        await SetCatalogActiveAsync(CustomerTransactionsId, active: false);

        var response = await TenantHttp.Send(
            HttpMethod.Get, $"/tenants/{tenant.PublicId}/reports", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var granted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = granted.RootElement.EnumerateArray().Single();
        item.GetProperty("id").GetGuid().ShouldBe(CustomerTransactionsId);
        item.GetProperty("code").GetString().ShouldBe("customer-transactions");
        item.GetProperty("name").GetString().ShouldBe("Customer Transactions Report");
        item.GetProperty("function").GetString().ShouldBe("Transactions in a date range for one customer.");
        item.GetProperty("scope").GetString().ShouldBe("Customer");
        item.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        item.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
            .ShouldBe(["manual", "scheduled"]);
        item.TryGetProperty("tenantId", out _).ShouldBeFalse();
    }

    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

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
