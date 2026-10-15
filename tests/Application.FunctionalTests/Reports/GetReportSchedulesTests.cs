using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GetReportSchedulesTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task List_FiltersByTenantScopeAndCaller_AndKeepsDisabledRows()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule List", "firm-schedule-list");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-list", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-list", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-schedule-list", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-list", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-schedule-list", "Password1!", tenant.Id, otherAdviser.Id);
        var south = await TestApp.CreateTenantAsync("South Schedule List", "south-schedule-list");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sue", "sue@south-schedule-list", "Password1!", south.Id);
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-schedule-list", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-schedule-list", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(tenant.PublicId);
        await GrantAsync(south.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-list", "Password1!", "firm-schedule-list");
        var assignedId = await CreateAsync(admin.AccessToken, customer.PublicId, "0 9 1 * *", true);
        var unassignedId = await CreateAsync(admin.AccessToken, otherCustomer.PublicId, "15 8 * * 1", true);
        var disabledId = await CreateAsync(admin.AccessToken, customer.PublicId, "30 7 * * 1", false);
        var southAdmin = await FunctionalTestSetup.Oidc.SignInAsync(
            "sue@south-schedule-list", "Password1!", "south-schedule-list");
        var southId = await CreateAsync(southAdmin.AccessToken, southCustomer.PublicId, "0 6 1 * *", true);

        var listed = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules", admin.AccessToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminItems = await ReadAsync(listed);
        adminItems.Select(item => item.Id).ShouldBe([assignedId, unassignedId, disabledId]);
        adminItems.Select(item => item.Id).ShouldNotContain(southId);
        var assigned = adminItems[0];
        assigned.ReportId.ShouldBe(CustomerTransactionsId);
        assigned.Cron.ShouldBe("0 9 1 * *");
        assigned.IsActive.ShouldBeTrue();
        assigned.RowVersion.ShouldNotBeNullOrWhiteSpace();
        assigned.CustomerId.ShouldBe(customer.PublicId);
        assigned.IncludeClosedAccounts.ShouldBeFalse();
        adminItems[2].Id.ShouldBe(disabledId);
        adminItems[2].IsActive.ShouldBeFalse();

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-schedule-list", "Password1!", "firm-schedule-list");
        var adviserItems = await ReadAsync(await TenantHttp.Send(
            HttpMethod.Get, "/reports/schedules", adviserTokens.AccessToken));
        adviserItems.Select(item => item.Id).ShouldBe([assignedId, disabledId]);

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var customerItems = await ReadAsync(await TenantHttp.Send(
            HttpMethod.Get, "/reports/schedules", customerTokens.AccessToken));
        customerItems.Select(item => item.Id).ShouldBe([assignedId, disabledId]);

        var otherTokens = await TestApp.IssueAccessTokenAsync(otherCustomer);
        var otherItems = await ReadAsync(await TenantHttp.Send(
            HttpMethod.Get, "/reports/schedules", otherTokens.AccessToken));
        otherItems.Select(item => item.Id).ShouldBe([unassignedId]);

        (await TenantHttp.Send(HttpMethod.Get, "/reports/schedules?scope=Book", customerTokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var book = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules?scope=Book", admin.AccessToken);
        book.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(book)).ShouldBeEmpty();
        var badScope = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules?scope=Nope", admin.AccessToken);
        badScope.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateAsync(
        string accessToken,
        Guid customerId,
        string cron,
        bool isActive)
    {
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/schedules",
            accessToken,
            new
            {
                reportId = CustomerTransactionsId,
                parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
                cron,
                isActive
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<ScheduleItem>> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item =>
        {
            var parameters = item.GetProperty("parameters");
            return new ScheduleItem(
                item.GetProperty("id").GetGuid(),
                item.GetProperty("reportId").GetGuid(),
                item.GetProperty("cron").GetString()!,
                item.GetProperty("isActive").GetBoolean(),
                item.GetProperty("rowVersion").GetString(),
                parameters.GetProperty("customerId").GetGuid(),
                parameters.GetProperty("includeClosedAccounts").GetBoolean());
        }).ToList();
    }

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

    private sealed record ScheduleItem(
        Guid Id,
        Guid ReportId,
        string Cron,
        bool IsActive,
        string? RowVersion,
        Guid CustomerId,
        bool IncludeClosedAccounts);
}
