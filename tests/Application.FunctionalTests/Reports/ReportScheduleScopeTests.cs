using System.Net;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class ReportScheduleScopeTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task CallerMustBeAllowed_AndAdviserOnlySchedulesAssignedCustomers()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Scope", "firm-schedule-scope");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-scope", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-scope", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-schedule-scope", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-scope", "Password1!", tenant.Id, adviser.Id);
        var unassigned = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-schedule-scope", "Password1!", tenant.Id, otherAdviser.Id);
        var south = await TestApp.CreateTenantAsync("South Schedule Scope", "south-schedule-scope");
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-schedule-scope", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-schedule-scope", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(tenant.PublicId);

        var anonymous = await PostAsync(null, customer.PublicId);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.Location.ShouldBeNull();

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var system = await TenantHttp.SignInSystemAdmin();
        (await PostAsync(customerTokens.AccessToken, customer.PublicId)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync(system.AccessToken, customer.PublicId)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-schedule-scope", "Password1!", "firm-schedule-scope");
        (await PostAsync(adviserTokens.AccessToken, unassigned.PublicId)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(adviserTokens.AccessToken, southCustomer.PublicId)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var created = await PostAsync(adviserTokens.AccessToken, customer.PublicId);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static object Body(Guid customerId) => new
    {
        reportId = CustomerTransactionsId,
        parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
        cron = "0 9 1 * *",
        isActive = true
    };

    private static Task<HttpResponseMessage> PostAsync(string? accessToken, Guid customerId) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, Body(customerId));

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
}
