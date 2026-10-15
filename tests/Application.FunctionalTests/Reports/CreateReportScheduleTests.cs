using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class CreateReportScheduleTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task TenantAdmin_CreatesTwoActiveSchedules_AndRegistersEachByPublicId()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule", "firm-schedule");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-schedule", "Password1!", "firm-schedule");

        var first = await PostAsync(tokens.AccessToken, customer.PublicId, "0 9 1 * *", "2026-01-01", "2026-03-31");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstId = firstJson.RootElement.GetProperty("id").GetGuid();

        var second = await PostAsync(tokens.AccessToken, customer.PublicId, "15 8 * * 1", "2026-04-01", "2026-06-30");
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondId = secondJson.RootElement.GetProperty("id").GetGuid();

        firstId.ShouldNotBe(secondId);
        var rows = await ReadSchedulesAsync(tenant.Id);
        rows.Count.ShouldBe(2);
        rows[0].PublicId.ShouldBe(firstId);
        rows[0].ReportId.ShouldBe(CustomerTransactionsId);
        rows[0].IsActive.ShouldBeTrue();
        rows[0].Cron.ShouldBe("0 9 1 * *");
        rows[0].Parameters.ShouldBe(Snapshot(customer.PublicId, "2026-01-01", "2026-03-31"));
        rows[1].PublicId.ShouldBe(secondId);
        rows[1].ReportId.ShouldBe(CustomerTransactionsId);
        rows[1].IsActive.ShouldBeTrue();
        rows[1].Cron.ShouldBe("15 8 * * 1");
        rows[1].Parameters.ShouldBe(Snapshot(customer.PublicId, "2026-04-01", "2026-06-30"));

        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>();
        scheduler.Recurring.ShouldBe([(firstId, "0 9 1 * *"), (secondId, "15 8 * * 1")]);
        scheduler.Enqueued.ShouldBeEmpty();
        (await CountExecutionsAsync(tenant.Id)).ShouldBe(0);
    }

    [Test]
    public async Task BadParameters_Return400_AndDoNotRegister()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Schedule Bad", "firm-schedule-bad");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-schedule-bad", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-schedule-bad", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-schedule-bad", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var tokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-schedule-bad", "Password1!", "firm-schedule-bad");
        var parameters = new { customerId = customer.PublicId, from = "2026-01-01", to = "2026-03-31" };

        var cron = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters,
            cron = "not-a-cron",
            isActive = true
        });
        cron.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await cron.Content.ReadAsStringAsync()).ShouldContain("cron");

        var seconds = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters,
            cron = "0 0 9 1 * *",
            isActive = true
        });
        seconds.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var longCron = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters,
            cron = new string('1', 101),
            isActive = true
        });
        longCron.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await longCron.Content.ReadAsStringAsync()).ShouldContain("100");

        var missingCron = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters,
            isActive = true
        });
        missingCron.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingCron.Content.ReadAsStringAsync()).ShouldContain("cron");

        var missingActive = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters,
            cron = "0 9 1 * *"
        });
        missingActive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingActive.Content.ReadAsStringAsync()).ShouldContain("isActive");

        var missingReport = await PostBodyAsync(tokens.AccessToken, new
        {
            parameters,
            cron = "0 9 1 * *",
            isActive = true
        });
        missingReport.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingReport.Content.ReadAsStringAsync()).ShouldContain("reportId");

        var notObject = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = "nope",
            cron = "0 9 1 * *",
            isActive = true
        });
        notObject.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notObject.Content.ReadAsStringAsync()).ShouldContain("object");

        var missingFrom = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive = true
        });
        missingFrom.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingFrom.Content.ReadAsStringAsync()).ShouldContain("from");

        var unknown = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new
            {
                customerId = customer.PublicId,
                from = "2026-01-01",
                to = "2026-03-31",
                extra = "no"
            },
            cron = "0 9 1 * *",
            isActive = true
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("unknown");

        var reversed = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, from = "2026-04-01", to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive = true
        });
        reversed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reversed.Content.ReadAsStringAsync()).ShouldContain("from");

        var oversized = await PostBodyAsync(tokens.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new
            {
                customerId = customer.PublicId,
                from = "2026-01-01",
                to = "2026-03-31",
                extra = new string('x', 9000)
            },
            cron = "0 9 1 * *",
            isActive = true
        });
        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await oversized.Content.ReadAsStringAsync()).ShouldContain("8 KB");

        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Recurring
            .ShouldBeEmpty();
        (await ReadSchedulesAsync(tenant.Id)).ShouldBeEmpty();
    }

    private static string Snapshot(Guid customerId, string from, string to) =>
        $$"""{"customerId":"{{customerId}}","from":"{{from}}","to":"{{to}}","includeClosedAccounts":false}""";

    private static object Body(Guid customerId, string cron, string from, string to) => new
    {
        reportId = CustomerTransactionsId,
        parameters = new { customerId, from, to },
        cron,
        isActive = true
    };

    private static Task<HttpResponseMessage> PostBodyAsync(string accessToken, object body) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, body);

    private static Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Guid customerId,
        string cron,
        string from,
        string to) =>
        TenantHttp.Send(
            HttpMethod.Post,
            "/reports/schedules",
            accessToken,
            Body(customerId, cron, from, to));

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

    private static async Task<List<ScheduleRow>> ReadSchedulesAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT s.[PublicId], r.[PublicId], s.[ParametersJson], s.[IsActive], s.[Cron]
            FROM [ReportSchedules] s
            JOIN [Reports] r ON r.[Id] = s.[ReportId]
            WHERE s.[TenantId] = @tenantId
            ORDER BY s.[Id]
            """;
        var id = command.CreateParameter();
        id.ParameterName = "@tenantId";
        id.Value = tenantId;
        command.Parameters.Add(id);
        var rows = new List<ScheduleRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ScheduleRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private static async Task<int> CountExecutionsAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [ReportExecutions] WHERE [TenantId] = @tenantId";
        var id = command.CreateParameter();
        id.ParameterName = "@tenantId";
        id.Value = tenantId;
        command.Parameters.Add(id);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed record ScheduleRow(
        Guid PublicId,
        Guid ReportId,
        string Parameters,
        bool IsActive,
        string Cron);
}
