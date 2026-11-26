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

[Parallelizable]
public class GetReportExecutionTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    private static readonly DateTimeOffset BookAsOf = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task Get_ReturnsOneExecution_WithListItemShape_AndFollowsTheJob()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Execution Get", "firm-execution-get");
        var admin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-execution-get", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-get", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-get", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var adminTokens = await TestApp.IssueAccessTokenAsync(admin);

        var executionId = await RequestAsync(adminTokens.AccessToken, customer.PublicId);
        var created = await ReadCreatedAsync(executionId);

        var queued = await GetAsync(adminTokens.AccessToken, executionId);
        queued.StatusCode.ShouldBe(HttpStatusCode.OK);
        queued.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using (var json = JsonDocument.Parse(await queued.Content.ReadAsStringAsync()))
        {
            var item = json.RootElement;
            item.GetProperty("id").GetGuid().ShouldBe(executionId);
            item.GetProperty("reportId").GetGuid().ShouldBe(CustomerTransactionsId);
            item.GetProperty("status").GetString().ShouldBe("Queued");
            item.GetProperty("triggerKind").GetString().ShouldBe("Manual");
            item.GetProperty("created").GetDateTimeOffset().ShouldBe(created);
            item.TryGetProperty("asOf", out _).ShouldBeFalse();
            var parameters = item.GetProperty("parameters");
            parameters.GetProperty("customerId").GetGuid().ShouldBe(customer.PublicId);
            parameters.GetProperty("from").GetString().ShouldBe("2026-01-01");
            parameters.GetProperty("to").GetString().ShouldBe("2026-03-31");
        }

        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        }

        var succeeded = await GetAsync(adminTokens.AccessToken, executionId);
        succeeded.StatusCode.ShouldBe(HttpStatusCode.OK);
        var succeededBody = await succeeded.Content.ReadAsStringAsync();
        succeededBody.ShouldNotContain("resultStorageKey");
        using (var json = JsonDocument.Parse(succeededBody))
        {
            json.RootElement.GetProperty("status").GetString().ShouldBe("Succeeded");
        }

        var bookId = await InsertBookExecutionAsync(
            tenant.Id, adviser.PublicId, created.AddHours(-1), BookAsOf, "firm/book.pdf");
        var book = await GetAsync(adminTokens.AccessToken, bookId);
        book.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bookBody = await book.Content.ReadAsStringAsync();
        bookBody.ShouldNotContain("resultStorageKey");
        bookBody.ShouldNotContain("errorMessage");
        bookBody.ShouldNotContain("firm/book.pdf");
        using (var json = JsonDocument.Parse(bookBody))
        {
            var item = json.RootElement;
            item.GetProperty("id").GetGuid().ShouldBe(bookId);
            item.GetProperty("reportId").GetGuid().ShouldBe(AdviserBalancesId);
            item.GetProperty("status").GetString().ShouldBe("Succeeded");
            item.GetProperty("triggerKind").GetString().ShouldBe("Scheduled");
            item.GetProperty("asOf").GetDateTimeOffset().ShouldBe(BookAsOf);
            item.GetProperty("parameters").GetProperty("adviserId").GetGuid().ShouldBe(adviser.PublicId);
        }
    }

    [Test]
    public async Task Get_OutOfScope_Returns404_AndInScope_Returns200()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Execution Get Scope", "firm-execution-get-scope");
        var admin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-execution-get-scope", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-get-scope", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-execution-get-scope", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-get-scope", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-execution-get-scope", "Password1!", tenant.Id, otherAdviser.Id);
        await GrantAsync(tenant.PublicId);
        var adminTokens = await TestApp.IssueAccessTokenAsync(admin);
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        var otherAdviserTokens = await TestApp.IssueAccessTokenAsync(otherAdviser);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        var otherCustomerTokens = await TestApp.IssueAccessTokenAsync(otherCustomer);

        var customerExecutionId = await RequestAsync(adminTokens.AccessToken, customer.PublicId);
        var now = DateTimeOffset.UtcNow;
        var bookExecutionId = await InsertBookExecutionAsync(tenant.Id, adviser.PublicId, now, BookAsOf, "own.pdf");
        var unreadableId = await InsertRawExecutionAsync(tenant.Id, "customer-transactions", "not-json", now);

        var south = await TestApp.CreateTenantAsync("South Execution Get", "south-execution-get");
        var southAdmin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sol", "sol@south-execution-get", "Password1!", south.Id);
        var southTokens = await TestApp.IssueAccessTokenAsync(southAdmin);
        (await GetAsync(southTokens.AccessToken, customerExecutionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(southTokens.AccessToken, bookExecutionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adminTokens.AccessToken, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adminTokens.AccessToken, unreadableId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await GetAsync(otherAdviserTokens.AccessToken, customerExecutionId)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(otherCustomerTokens.AccessToken, customerExecutionId)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adminTokens.AccessToken, customerExecutionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(adviserTokens.AccessToken, customerExecutionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(customerTokens.AccessToken, customerExecutionId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetAsync(otherAdviserTokens.AccessToken, bookExecutionId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(adviserTokens.AccessToken, bookExecutionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(adminTokens.AccessToken, bookExecutionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Get_WrongPolicy_Returns403_AndAnonymous_Returns401()
    {
        (await GetAsync(null, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var system = await TenantHttp.SignInSystemAdmin();
        (await GetAsync(system.AccessToken, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tenant = await TestApp.CreateTenantAsync("Firm Execution Get Policy", "firm-execution-get-policy");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-get-policy", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-get-policy", "Password1!", tenant.Id, adviser.Id);
        var bookExecutionId = await InsertBookExecutionAsync(
            tenant.Id, adviser.PublicId, DateTimeOffset.UtcNow, BookAsOf, "book.pdf");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        (await GetAsync(customerTokens.AccessToken, bookExecutionId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
                parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" }
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> GetAsync(string? accessToken, Guid executionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/reports/executions/{executionId}");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return FunctionalTestSetup.WebClient.SendAsync(request);
    }

    private static async Task<DateTimeOffset> ReadCreatedAsync(Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportExecutions.AsNoTracking()
            .Where(row => row.PublicId == executionId)
            .Select(row => row.Created)
            .SingleAsync();
    }

    private static async Task<Guid> InsertRawExecutionAsync(
        int tenantId,
        string reportCode,
        string parameters,
        DateTimeOffset created)
    {
        var executionId = Guid.NewGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportId = await db.Reports
            .Where(row => row.Code == reportCode)
            .Select(row => row.Id)
            .SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [ReportExecutions]
                ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson],
                 [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES
                ({executionId}, {reportId}, {tenantId}, N'Manual', N'Queued', {parameters},
                 {created}, N'test', {created}, N'test')
            """);
        return executionId;
    }

    private static async Task<Guid> InsertBookExecutionAsync(
        int tenantId,
        Guid adviserId,
        DateTimeOffset created,
        DateTimeOffset asOf,
        string storageKey)
    {
        var executionId = Guid.NewGuid();
        var parameters = $$"""{"adviserId":"{{adviserId}}","includeClosedAccounts":false}""";
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportId = await db.Reports
            .Where(row => row.Code == "adviser-balances")
            .Select(row => row.Id)
            .SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [ReportExecutions]
                ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson], [AsOf],
                 [ResultStorageKey], [ErrorMessage], [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES
                ({executionId}, {reportId}, {tenantId}, N'Scheduled', N'Succeeded', {parameters}, {asOf},
                 {storageKey}, N'hidden', {created}, N'test', {created}, N'test')
            """);
        return executionId;
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
}
