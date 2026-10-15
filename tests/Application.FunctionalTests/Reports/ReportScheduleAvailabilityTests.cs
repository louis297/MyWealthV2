using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class ReportScheduleAvailabilityTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task MissingGrantInactiveDisallowedUnknownOrOtherReport_Returns404()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Missing", "firm-schedule-missing");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-missing", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-missing", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-missing", "Password1!", tenant.Id, adviser.Id);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-missing", "Password1!", "firm-schedule-missing");
        var body = Body(CustomerTransactionsId, customer.PublicId);

        var missing = await PostAsync(tokens.AccessToken, body);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain(CustomerTransactionsId.ToString());

        await GrantAsync(tenant.PublicId, CustomerTransactionsId);
        await SetCatalogAsync(CustomerTransactionsId, active: false, triggers: """["manual","scheduled"]""");
        var inactive = await PostAsync(tokens.AccessToken, body);
        inactive.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetCatalogAsync(CustomerTransactionsId, active: true, triggers: """["manual"]""");
        var trigger = await PostAsync(tokens.AccessToken, body);
        trigger.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unknownId = Guid.NewGuid();
        var unknown = await PostAsync(tokens.AccessToken, Body(unknownId, customer.PublicId));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain(unknownId.ToString());

        await SetCatalogAsync(CustomerTransactionsId, active: true, triggers: """["manual","scheduled"]""");
        await GrantAsync(tenant.PublicId, AdviserBalancesId);
        var balances = await PostAsync(tokens.AccessToken, Body(AdviserBalancesId, customer.PublicId));
        balances.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await balances.Content.ReadAsStringAsync()).ShouldContain("unknown");

        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Recurring
            .ShouldBeEmpty();
    }

    private static object Body(Guid reportId, Guid customerId) => new
    {
        reportId,
        parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
        cron = "0 9 1 * *",
        isActive = true
    };

    private static Task<HttpResponseMessage> PostAsync(string accessToken, object body) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, body);

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

    private static async Task SetCatalogAsync(Guid reportPublicId, bool active, string triggers)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active}, [AllowedTriggers] = {triggers} WHERE [PublicId] = {reportPublicId}");
    }
}
