using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class UpdateReportScheduleTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task Update_ReplacesTheDefinition_AndRejectsStaleOrOutOfScopeChanges()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Update", "firm-schedule-update");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-update", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-update", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-schedule-update", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-update", "Password1!", tenant.Id, adviser.Id);
        var unassigned = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-schedule-update", "Password1!", tenant.Id, otherAdviser.Id);
        var south = await TestApp.CreateTenantAsync("South Schedule Update", "south-schedule-update");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sue", "sue@south-schedule-update", "Password1!", south.Id);
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-schedule-update", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-schedule-update", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(tenant.PublicId, CustomerTransactionsId);
        await GrantAsync(tenant.PublicId, AdviserBalancesId);
        await GrantAsync(south.PublicId, CustomerTransactionsId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-update", "Password1!", "firm-schedule-update");
        var scheduleId = await CreateAsync(admin.AccessToken, customer.PublicId, "0 9 1 * *");
        var hiddenId = await CreateAsync(admin.AccessToken, unassigned.PublicId, "0 9 1 * *");
        var southAdmin = await FunctionalTestSetup.Oidc.SignInAsync(
            "sue@south-schedule-update", "Password1!", "south-schedule-update");
        var southId = await CreateAsync(southAdmin.AccessToken, southCustomer.PublicId, "0 9 1 * *");
        var rowVersion = (await ListedAsync(admin.AccessToken)).Single(item => item.Id == scheduleId).RowVersion;

        var updated = await PutAsync(admin.AccessToken, scheduleId, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "15 8 * * 1",
            isActive = false,
            rowVersion
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await ListedAsync(admin.AccessToken)).Single(item => item.Id == scheduleId);
        after.Cron.ShouldBe("15 8 * * 1");
        after.IsActive.ShouldBeFalse();
        after.From.ShouldBe("2026-02-01");
        after.To.ShouldBe("2026-05-01");
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>();
        scheduler.Recurring.ShouldContain((scheduleId, "15 8 * * 1"));
        var recurringAfterUpdate = scheduler.Recurring.ToList();

        var missingVersion = await PutAsync(admin.AccessToken, scheduleId, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "15 8 * * 1",
            isActive = false
        });
        missingVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var stale = await PutAsync(admin.AccessToken, scheduleId, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "0 1 * * *",
            isActive = true,
            rowVersion
        });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await PutAsync(admin.AccessToken, southId, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = southCustomer.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "0 1 * * *",
            isActive = true,
            rowVersion = after.RowVersion
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await PutAsync(admin.AccessToken, scheduleId, new
        {
            reportId = AdviserBalancesId,
            parameters = new { customerId = customer.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "0 1 * * *",
            isActive = true,
            rowVersion = after.RowVersion
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-schedule-update", "Password1!", "firm-schedule-update");
        var hiddenVersion = (await ListedAsync(admin.AccessToken)).Single(item => item.Id == hiddenId).RowVersion;
        (await PutAsync(adviserTokens.AccessToken, hiddenId, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = unassigned.PublicId, from = "2026-02-01", to = "2026-05-01" },
            cron = "0 1 * * *",
            isActive = true,
            rowVersion = hiddenVersion
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        scheduler.Recurring.ShouldBe(recurringAfterUpdate);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid customerId, string cron)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
            cron,
            isActive = true
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PutAsync(string accessToken, Guid id, object body) =>
        TenantHttp.Send(HttpMethod.Put, $"/reports/schedules/{id}", accessToken, body);

    private static async Task<List<ScheduleItem>> ListedAsync(string accessToken)
    {
        var response = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item =>
        {
            var parameters = item.GetProperty("parameters");
            return new ScheduleItem(
                item.GetProperty("id").GetGuid(),
                item.GetProperty("cron").GetString()!,
                item.GetProperty("isActive").GetBoolean(),
                item.GetProperty("rowVersion").GetString()!,
                parameters.GetProperty("from").GetString()!,
                parameters.GetProperty("to").GetString()!);
        }).ToList();
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

    private sealed record ScheduleItem(
        Guid Id,
        string Cron,
        bool IsActive,
        string RowVersion,
        string From,
        string To);
}
