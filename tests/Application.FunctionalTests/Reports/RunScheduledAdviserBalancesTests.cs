using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RunScheduledAdviserBalancesTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task RunScheduled_SetsAsOfAndExecuteWritesThePdf_AndSkipsInactiveRevokedOrMissingAdviser()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Book Fire", "firm-book-fire");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-book-fire", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann Book", "ann@firm-book-fire", "Password1!", tenant.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-book-fire", "Password1!", "firm-book-fire");
        var activeId = await CreateAsync(admin.AccessToken, adviser.PublicId, isActive: true);
        var inactiveId = await CreateAsync(admin.AccessToken, adviser.PublicId, isActive: false);
        var missingId = await CreateAsync(admin.AccessToken, adviser.PublicId, isActive: true);
        await ReplaceAdviserAsync(missingId, Guid.NewGuid());

        var before = DateTimeOffset.UtcNow;
        await RunAsync(activeId);
        var after = DateTimeOffset.UtcNow;

        var execution = await ReadExecutionAsync(activeId);
        execution.TriggerKind.ShouldBe("Scheduled");
        execution.Status.ShouldBe("Queued");
        execution.AsOf.ShouldNotBeNull();
        execution.AsOf!.Value.Offset.ShouldBe(TimeSpan.Zero);
        execution.AsOf.Value.ShouldBeGreaterThanOrEqualTo(before);
        execution.AsOf.Value.ShouldBeLessThanOrEqualTo(after);
        execution.TriggeredByUserId.ShouldBeNull();
        execution.ScheduleId.ShouldBe(execution.ScheduleRowId);
        execution.Parameters.ShouldBe(
            $$"""{"adviserId":"{{adviser.PublicId}}","includeClosedAccounts":false}""");
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued
            .ShouldBe([execution.PublicId]);

        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(execution.PublicId);
        }

        var generated = await ReadExecutionAsync(activeId);
        generated.Status.ShouldBe("Succeeded");
        generated.ResultStorageKey.ShouldBe($"{tenant.PublicId:D}/{execution.PublicId:D}.pdf");
        var path = Path.Combine(
            ReportStoragePaths.Root,
            tenant.PublicId.ToString("D"),
            $"{execution.PublicId:D}.pdf");
        var pdf = await File.ReadAllBytesAsync(path);
        Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
        var text = ReadPdf(pdf);
        text.ShouldContain("Adviser Balances Report");
        text.ShouldContain("Adviser: Ann Book");
        text.ShouldContain("No balances.");
        text.ShouldContain(
            $"As of: {execution.AsOf.Value.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC");

        await RunAsync(inactiveId);
        await RunAsync(missingId);
        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{tenant.PublicId}/reports/{AdviserBalancesId}",
            system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await RunAsync(activeId);

        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued
            .ShouldBe([execution.PublicId]);
        (await CountExecutionsAsync(tenant.Id)).ShouldBe(1);
    }

    private static async Task RunAsync(Guid scheduleId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().RunScheduled(scheduleId);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid adviserId, bool isActive)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId },
            cron = "0 9 1 * *",
            isActive
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task ReplaceAdviserAsync(Guid scheduleId, Guid adviserId)
    {
        var parameters = $$"""{"adviserId":"{{adviserId}}","includeClosedAccounts":false}""";
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [ReportSchedules]
            SET [ParametersJson] = {parameters}
            WHERE [PublicId] = {scheduleId}
            """);
        updated.ShouldBe(1);
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
            execution.ParametersJson,
            execution.ResultStorageKey);
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
            new { reportId = AdviserBalancesId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static string ReadPdf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }

    private sealed record ExecutionRow(
        Guid PublicId,
        string TriggerKind,
        string Status,
        DateTimeOffset? AsOf,
        int? TriggeredByUserId,
        int? ScheduleId,
        int ScheduleRowId,
        string Parameters,
        string? ResultStorageKey);
}
