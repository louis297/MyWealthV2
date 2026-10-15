using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RequestReportValidationTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task CustomerAndSystemAdmin_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Request", "firm-request");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-request", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-request", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var body = ValidBody(customer.PublicId);

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var system = await TenantHttp.SignInSystemAdmin();

        (await TenantHttp.Send(HttpMethod.Post, "/reports/request", customerTokens.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Post, "/reports/request", system.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task BadParameters_Return400()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Bad", "firm-bad");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-bad", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-bad", "Password1!", tenant.Id);
        var target = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-bad", "Password1!", tenant.Id, customer.Id);
        await GrantAsync(tenant.PublicId);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-bad", "Password1!", "firm-bad");

        var missingFrom = await PostAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = target.PublicId, to = "2026-03-31" }
        });
        missingFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingFrom.Content.ReadAsStringAsync()).ShouldContain("from");

        var unknown = await PostAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new
            {
                customerId = target.PublicId,
                from = "2026-01-01",
                to = "2026-03-31",
                extra = "no"
            }
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("unknown");

        var reversed = await PostAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = target.PublicId, from = "2026-04-01", to = "2026-03-31" }
        });
        reversed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reversed.Content.ReadAsStringAsync()).ShouldContain("from");

        var oversized = await PostAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new
            {
                customerId = target.PublicId,
                from = "2026-01-01",
                to = "2026-03-31",
                extra = new string('x', 9000)
            }
        });
        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await oversized.Content.ReadAsStringAsync()).ShouldContain("8 KB");
    }

    [Test]
    public async Task MissingGrantInactiveOrDisallowedTrigger_Returns404()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Missing", "firm-missing");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-missing", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-missing", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-missing", "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-missing", "Password1!", "firm-missing");
        var body = ValidBody(customer.PublicId);

        var missing = await PostAsync(tokens.AccessToken, body);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain(CustomerTransactionsId.ToString());

        await GrantAsync(tenant.PublicId);
        await SetCatalogAsync(CustomerTransactionsId, active: false, triggers: """["manual","scheduled"]""");
        var inactive = await PostAsync(tokens.AccessToken, body);
        inactive.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetCatalogAsync(CustomerTransactionsId, active: true, triggers: """["event"]""");
        var trigger = await PostAsync(tokens.AccessToken, body);
        trigger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object ValidBody(Guid customerId) => new
    {
        reportId = CustomerTransactionsId,
        parameters = new
        {
            customerId,
            from = "2026-01-01",
            to = "2026-03-31"
        }
    };

    private static Task<HttpResponseMessage> PostAsync(string accessToken, object body) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/request", accessToken, body);

    private static async Task GrantAsync(Guid tenantId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            system.AccessToken,
            new { reportId = CustomerTransactionsId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task SetCatalogAsync(Guid reportPublicId, bool active, string triggers)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active}, [AllowedTriggers] = {triggers} WHERE [PublicId] = {reportPublicId}");
    }
}
