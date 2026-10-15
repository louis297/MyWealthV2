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

    [Test]
    public async Task Download_OutOfScope_Returns404_AndInScope_Returns200()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Scope", "firm-scope");
        var admin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-scope", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-scope", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-scope", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-scope", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-scope", "Password1!", tenant.Id, otherAdviser.Id);
        await GrantAsync(tenant.PublicId);
        var adminTokens = await TestApp.IssueAccessTokenAsync(admin);
        var executionId = await RequestAsync(adminTokens.AccessToken, customer.PublicId);
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var south = await TestApp.CreateTenantAsync("South Scope", "south-scope");
        var southAdmin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sol", "sol@south-scope", "Password1!", south.Id);
        var southTokens = await TestApp.IssueAccessTokenAsync(southAdmin);
        (await GetAsync(southTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adminTokens.AccessToken, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var otherAdviserTokens = await TestApp.IssueAccessTokenAsync(otherAdviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var otherCustomerTokens = await TestApp.IssueAccessTokenAsync(otherCustomer);
        (await GetAsync(otherAdviserTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(otherCustomerTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adviserTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(adminTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(customerTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Download_WrongPolicy_Returns403_AndAnonymous_Returns401()
    {
        (await GetAsync(null, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var system = await TenantHttp.SignInSystemAdmin();
        (await GetAsync(system.AccessToken, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tenant = await TestApp.CreateTenantAsync("Firm Policy", "firm-policy");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-policy", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-policy", "Password1!", tenant.Id, adviser.Id);
        var executionId = await QueueBookExecutionAsync(tenant.Id);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        (await GetAsync(customerTokens.AccessToken, executionId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> QueueBookExecutionAsync(int tenantId)
    {
        var executionId = Guid.NewGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportId = await db.Reports
            .Where(row => row.Code == "adviser-balances")
            .Select(row => row.Id)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var parameters = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [ReportExecutions]
                ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson],
                 [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES
                ({executionId}, {reportId}, {tenantId}, N'Manual', N'Queued', {parameters},
                 {now}, N'test', {now}, N'test')
            """);
        return executionId;
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
