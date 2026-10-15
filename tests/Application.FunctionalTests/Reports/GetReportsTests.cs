using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GetReportsTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/reports", accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task SystemAdmin_Returns403()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();

        (await TenantHttp.Send(HttpMethod.Get, "/reports", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Customer_BookScope_Returns403_AndSeesOnlyActiveGrantedCustomerReports()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm List", "firm-list");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-list", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-list", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId, CustomerTransactionsId);
        await GrantAsync(tenant.PublicId, AdviserBalancesId);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Get, "/reports?scope=Book", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var response = await TenantHttp.Send(HttpMethod.Get, "/reports", tokens.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.EnumerateArray().Single();
        item.GetProperty("id").GetGuid().ShouldBe(CustomerTransactionsId);
        item.GetProperty("code").GetString().ShouldBe("customer-transactions");
        item.GetProperty("scope").GetString().ShouldBe("Customer");
        item.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
            .ShouldBe(["manual", "scheduled"]);
        item.TryGetProperty("isActive", out _).ShouldBeFalse();

        await SetCatalogActiveAsync(CustomerTransactionsId, active: false);
        var inactive = await TenantHttp.Send(HttpMethod.Get, "/reports", tokens.AccessToken);
        using var empty = JsonDocument.Parse(await inactive.Content.ReadAsStringAsync());
        empty.RootElement.GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task TenantAdmin_SeesOnlyThisTenantsActiveGrants()
    {
        var north = await TestApp.CreateTenantAsync("North List", "north-list");
        var south = await TestApp.CreateTenantAsync("South List", "south-list");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north-list", "Password1!", north.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sue", "sue@south-list", "Password1!", south.Id);
        await GrantAsync(north.PublicId, CustomerTransactionsId);
        await GrantAsync(south.PublicId, AdviserBalancesId);
        var northTokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@north-list", "Password1!", "north-list");
        var southTokens = await FunctionalTestSetup.Oidc.SignInAsync("sue@south-list", "Password1!", "south-list");

        var northList = await TenantHttp.Send(HttpMethod.Get, "/reports", northTokens.AccessToken);
        using var northJson = JsonDocument.Parse(await northList.Content.ReadAsStringAsync());
        northJson.RootElement.EnumerateArray().Select(item => item.GetProperty("code").GetString())
            .ShouldBe(["customer-transactions"]);

        var book = await TenantHttp.Send(HttpMethod.Get, "/reports?scope=Book", northTokens.AccessToken);
        book.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var bookJson = JsonDocument.Parse(await book.Content.ReadAsStringAsync());
        bookJson.RootElement.GetArrayLength().ShouldBe(0);

        var southList = await TenantHttp.Send(HttpMethod.Get, "/reports", southTokens.AccessToken);
        using var southJson = JsonDocument.Parse(await southList.Content.ReadAsStringAsync());
        southJson.RootElement.EnumerateArray().Select(item => item.GetProperty("code").GetString())
            .ShouldBe(["adviser-balances"]);
    }

    private static async Task GrantAsync(Guid tenantId, Guid reportId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            system.AccessToken,
            new { reportId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task SetCatalogActiveAsync(Guid reportPublicId, bool active)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active} WHERE [PublicId] = {reportPublicId}");
    }
}
