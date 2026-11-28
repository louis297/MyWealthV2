using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

[Parallelizable]
public class GetReportCatalogTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/reports/catalog", accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task OtherRoles_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Catalog List", "firm-catalog-list");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-catalog-list", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-catalog-list", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-catalog-list", "Password1!", tenant.Id, adviser.Id);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-catalog-list", "Password1!", "firm-catalog-list");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-catalog-list", "Password1!", "firm-catalog-list");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Get, "/reports/catalog", admin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, "/reports/catalog", adviserTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Get, "/reports/catalog", customerTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SystemAdmin_ListsEveryCatalogRow_IncludingInactive_WithoutGranted()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        await SetCatalogActiveAsync(CustomerTransactionsId, active: false);

        try
        {
            var response = await TenantHttp.Send(HttpMethod.Get, "/reports/catalog", tokens.AccessToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToList();
            items.Select(item => item.GetProperty("code").GetString())
                .ShouldBe(["adviser-balances", "bank-reconciliation", "customer-fif", "customer-income-summary", "customer-transactions"]);

            var adviser = items[0];
            adviser.GetProperty("id").GetGuid().ShouldBe(AdviserBalancesId);
            adviser.GetProperty("name").GetString().ShouldBe("Adviser Balances Report");
            adviser.GetProperty("function").GetString()
                .ShouldBe("Point-in-time cash and holding lines for an adviser's customers.");
            adviser.GetProperty("scope").GetString().ShouldBe("Book");
            adviser.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            adviser.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
                .ShouldBe(["manual", "scheduled"]);
            adviser.TryGetProperty("granted", out _).ShouldBeFalse();

            var bank = items[1];
            bank.GetProperty("scope").GetString().ShouldBe("Tenant");
            bank.GetProperty("name").GetString().ShouldBe("Bank reconciliation");

            var fif = items[2];
            fif.GetProperty("id").GetGuid().ShouldBe(Guid.Parse("7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44"));
            fif.GetProperty("name").GetString().ShouldBe("FIF Report");
            fif.GetProperty("scope").GetString().ShouldBe("Customer");
            fif.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            fif.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
                .ShouldBe(["manual"]);

            var income = items[3];
            income.GetProperty("id").GetGuid().ShouldBe(Guid.Parse("4b6e2d91-3f7a-4c18-9d52-8a1e7c0b6f55"));
            income.GetProperty("name").GetString().ShouldBe("Tax Year Income Summary");
            income.GetProperty("function").GetString().ShouldBe(
                "Dividends and interest paid to one customer in one New Zealand tax year, in New Zealand dollars, with the tax taken from them and the imputation credits.");
            income.GetProperty("scope").GetString().ShouldBe("Customer");
            income.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            income.GetProperty("allowedTriggers").EnumerateArray().Select(trigger => trigger.GetString())
                .ShouldBe(["manual"]);

            var customer = items[4];
            customer.GetProperty("id").GetGuid().ShouldBe(CustomerTransactionsId);
            customer.GetProperty("isActive").GetBoolean().ShouldBeFalse();
            customer.GetProperty("scope").GetString().ShouldBe("Customer");
            customer.TryGetProperty("granted", out _).ShouldBeFalse();
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
