using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RunScheduledReportTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task RunScheduled_QueuesAnExecution_AndSkipsInactiveRevokedOrUnknownSchedules()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Fire", "firm-schedule-fire");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-fire", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-fire", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-fire", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-fire", "Password1!", "firm-schedule-fire");
        var activeId = await CreateAsync(admin.AccessToken, customer.PublicId, isActive: true);
        var inactiveId = await CreateAsync(admin.AccessToken, customer.PublicId, isActive: false);

        await RunAsync(activeId);

        var execution = await ReadExecutionAsync(activeId);
        execution.TriggerKind.ShouldBe("Scheduled");
        execution.Status.ShouldBe("Queued");
        execution.AsOf.ShouldBeNull();
        execution.TriggeredByUserId.ShouldBeNull();
        execution.ScheduleId.ShouldBe(execution.ScheduleRowId);
        execution.Parameters.ShouldBe(
            $$"""{"customerId":"{{customer.PublicId}}","from":"2026-01-01","to":"2026-03-31","includeClosedAccounts":false}""");
        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>();
        scheduler.Enqueued.ShouldBe([execution.PublicId]);

        await RunAsync(inactiveId);
        await RunAsync(Guid.NewGuid());
        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{tenant.PublicId}/reports/{CustomerTransactionsId}",
            system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await RunAsync(activeId);

        scheduler.Enqueued.ShouldBe([execution.PublicId]);
        (await CountExecutionsAsync(tenant.Id)).ShouldBe(1);
    }

    private static async Task RunAsync(Guid scheduleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().RunScheduled(scheduleId);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid customerId, bool isActive)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<ExecutionRow> ReadExecutionAsync(Guid schedulePublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedule = await db.ReportSchedules.AsNoTracking()
            .SingleAsync(row => row.PublicId == schedulePublicId);
        var execution = await db.ReportExecutions.AsNoTracking()
            .SingleAsync(row => row.ScheduleId == schedule.Id);
        return new ExecutionRow(
            execution.PublicId,
            execution.TriggerKind,
            execution.Status,
            execution.AsOf,
            execution.TriggeredByUserId,
            execution.ScheduleId,
            schedule.Id,
            execution.ParametersJson);
    }

    private static async Task<int> CountExecutionsAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportExecutions.CountAsync(row => row.TenantId == tenantId);
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

    private sealed record ExecutionRow(
        Guid PublicId,
        string TriggerKind,
        string Status,
        DateTimeOffset? AsOf,
        int? TriggeredByUserId,
        int? ScheduleId,
        int ScheduleRowId,
        string Parameters);
}
