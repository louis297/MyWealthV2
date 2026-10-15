using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GetReportExecutionsTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly DateTimeOffset BookAsOf = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task List_ReturnsTenantExecutions_WithSnapshotAndAsOf()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Execution List", "firm-execution-list");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-execution-list", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-list", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-list", "Password1!", tenant.Id, adviser.Id);
        var south = await TestApp.CreateTenantAsync("South Execution List", "south-execution-list");
        await GrantAsync(tenant.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-execution-list", "Password1!", "firm-execution-list");
        var customerExecutionId = await RequestAsync(admin.AccessToken, customer.PublicId);
        var customerCreated = await ReadCreatedAsync(customerExecutionId);
        var bookCreated = customerCreated.AddHours(-1);
        var bookExecutionId = await InsertBookExecutionAsync(
            tenant.Id,
            adviser.PublicId,
            bookCreated,
            BookAsOf,
            "firm/book.pdf");
        var southExecutionId = await InsertBookExecutionAsync(
            south.Id,
            Guid.NewGuid(),
            customerCreated.AddHours(1),
            BookAsOf,
            "south/book.pdf");

        var response = await GetAsync(admin.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("resultStorageKey");
        body.ShouldNotContain("errorMessage");
        body.ShouldNotContain("firm/book.pdf");
        body.ShouldNotContain(southExecutionId.ToString());

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("pageSize").GetInt32().ShouldBe(20);
        root.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);

        var customerItem = items[0];
        customerItem.GetProperty("id").GetGuid().ShouldBe(customerExecutionId);
        customerItem.GetProperty("reportId").GetGuid().ShouldBe(CustomerTransactionsId);
        customerItem.GetProperty("status").GetString().ShouldBe("Queued");
        customerItem.GetProperty("triggerKind").GetString().ShouldBe("Manual");
        customerItem.GetProperty("created").GetDateTimeOffset().ShouldBe(customerCreated);
        customerItem.TryGetProperty("asOf", out _).ShouldBeFalse();
        var customerParameters = customerItem.GetProperty("parameters");
        customerParameters.GetProperty("customerId").GetGuid().ShouldBe(customer.PublicId);
        customerParameters.GetProperty("from").GetString().ShouldBe("2026-01-01");
        customerParameters.GetProperty("to").GetString().ShouldBe("2026-03-31");
        customerParameters.GetProperty("includeClosedAccounts").GetBoolean().ShouldBeFalse();

        var bookItem = items[1];
        bookItem.GetProperty("id").GetGuid().ShouldBe(bookExecutionId);
        bookItem.GetProperty("reportId").GetGuid().ShouldBe(Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22"));
        bookItem.GetProperty("status").GetString().ShouldBe("Succeeded");
        bookItem.GetProperty("triggerKind").GetString().ShouldBe("Scheduled");
        bookItem.GetProperty("created").GetDateTimeOffset().ShouldBe(bookCreated);
        bookItem.GetProperty("asOf").GetDateTimeOffset().ShouldBe(BookAsOf);
        var bookParameters = bookItem.GetProperty("parameters");
        bookParameters.GetProperty("adviserId").GetGuid().ShouldBe(adviser.PublicId);
        bookParameters.GetProperty("includeClosedAccounts").GetBoolean().ShouldBeFalse();
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

    private static Task<HttpResponseMessage> GetAsync(string? accessToken, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/reports/executions" + query);
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
