using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class DisableReportScheduleTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task Disable_ClearsActiveAndRemovesTheJob_WithoutDeletingTheRow()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Disable", "firm-schedule-disable");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-disable", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-disable", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-schedule-disable", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-disable", "Password1!", tenant.Id, adviser.Id);
        var unassigned = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-schedule-disable", "Password1!", tenant.Id, otherAdviser.Id);
        var south = await TestApp.CreateTenantAsync("South Schedule Disable", "south-schedule-disable");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sue", "sue@south-schedule-disable", "Password1!", south.Id);
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-schedule-disable", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-schedule-disable", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(tenant.PublicId);
        await GrantAsync(south.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-disable", "Password1!", "firm-schedule-disable");
        var scheduleId = await CreateAsync(admin.AccessToken, customer.PublicId);
        var hiddenId = await CreateAsync(admin.AccessToken, unassigned.PublicId);
        var southAdmin = await FunctionalTestSetup.Oidc.SignInAsync(
            "sue@south-schedule-disable", "Password1!", "south-schedule-disable");
        var southId = await CreateAsync(southAdmin.AccessToken, southCustomer.PublicId);

        var disabled = await DisableAsync(admin.AccessToken, scheduleId);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var listed = await ListedAsync(admin.AccessToken);
        listed.Single(item => item.Id == scheduleId).IsActive.ShouldBeFalse();
        listed.Select(item => item.Id).ShouldBe([scheduleId, hiddenId]);

        var again = await DisableAsync(admin.AccessToken, scheduleId);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DisableAsync(admin.AccessToken, southId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-schedule-disable", "Password1!", "firm-schedule-disable");
        (await DisableAsync(adviserTokens.AccessToken, hiddenId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Removed
            .ShouldBe([scheduleId, scheduleId]);
        (await ListedAsync(admin.AccessToken)).Single(item => item.Id == hiddenId).IsActive.ShouldBeTrue();
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid customerId)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive = true
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> DisableAsync(string accessToken, Guid id) =>
        TenantHttp.Send(HttpMethod.Post, $"/reports/schedules/{id}/disable", accessToken);

    private static async Task<List<ScheduleItem>> ListedAsync(string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item => new ScheduleItem(
            item.GetProperty("id").GetGuid(),
            item.GetProperty("isActive").GetBoolean())).ToList();
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

    private sealed record ScheduleItem(Guid Id, bool IsActive);
}
