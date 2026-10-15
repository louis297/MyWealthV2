using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class DownloadReportTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task SucceededExecution_DownloadsThePdf()
    {
        var (tenant, executionId, token) = await SucceedAsync("firm-download", "tess@firm-download", "cee@firm-download");

        var response = await GetAsync(token, executionId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var body = await response.Content.ReadAsByteArrayAsync();
        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        body.ShouldBe(await File.ReadAllBytesAsync(path));
    }

    [Test]
    public async Task Download_WhenNotSucceeded_Returns409()
    {
        var code = "firm-ready";
        var tenant = await TestApp.CreateTenantAsync(code, code);
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@firm-ready", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firm-ready", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-ready", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-ready", "Password1!", code);

        var queued = await RequestAsync(admin.AccessToken, customer.PublicId);
        (await GetAsync(admin.AccessToken, queued)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var failed = await RequestAsync(admin.AccessToken, customer.PublicId);
        await ReplaceParametersAsync(failed, """{"unknown":true}""");
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(failed);
        (await ReadAsync(failed)).Status.ShouldBe("Failed");
        (await GetAsync(admin.AccessToken, failed)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task GrantAsync(Guid tenantId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var grant = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            system.AccessToken,
            new { reportId = CustomerTransactionsId });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> RequestAsync(string accessToken, Guid customerId)
    {
        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            accessToken,
            new
            {
                reportId = CustomerTransactionsId,
                parameters = new { customerId, from = "2026-01-01", to = "2026-02-01" }
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task ReplaceParametersAsync(Guid executionId, string parametersJson)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [ReportExecutions]
            SET [ParametersJson] = {parametersJson}
            WHERE [PublicId] = {executionId}
            """);
    }

    private static async Task<(Domain.Entities.Tenant Tenant, Guid ExecutionId, string AccessToken)> SucceedAsync(
        string code,
        string adminEmail,
        string customerEmail)
    {
        var tenant = await TestApp.CreateTenantAsync(code, code);
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", adminEmail, "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@" + code, "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", customerEmail, "Password1!", tenant.Id, adviser.Id);
        var system = await TenantHttp.SignInSystemAdmin();
        var grant = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenant.PublicId}/reports",
            system.AccessToken,
            new { reportId = CustomerTransactionsId });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync(adminEmail, "Password1!", code);
        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            admin.AccessToken,
            new
            {
                reportId = CustomerTransactionsId,
                parameters = new { customerId = customer.PublicId, from = "2026-01-01", to = "2026-02-01" }
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        var row = await ReadAsync(executionId);
        row.Status.ShouldBe("Succeeded");
        return (tenant, executionId, admin.AccessToken);
    }

    private static Task<HttpResponseMessage> GetAsync(string? accessToken, Guid executionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/reports/executions/{executionId}/download");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return FunctionalTestSetup.WebClient.SendAsync(request);
    }

    private static async Task<(string Status, string? ResultStorageKey)> ReadAsync(Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.AsNoTracking().SingleAsync(execution => execution.PublicId == executionId);
        return (row.Status, row.ResultStorageKey);
    }
}
