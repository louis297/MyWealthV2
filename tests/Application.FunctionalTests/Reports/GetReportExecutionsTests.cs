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

    [Test]
    public async Task List_FiltersByCallerScope()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Execution Scope", "firm-execution-scope");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-execution-scope", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-scope", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-execution-scope", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-scope", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-execution-scope", "Password1!", tenant.Id, otherAdviser.Id);
        await GrantAsync(tenant.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-execution-scope", "Password1!", "firm-execution-scope");
        var assignedId = await RequestAsync(admin.AccessToken, customer.PublicId);
        var unassignedId = await RequestAsync(admin.AccessToken, otherCustomer.PublicId);
        var now = DateTimeOffset.UtcNow;
        var ownBookId = await InsertBookExecutionAsync(tenant.Id, adviser.PublicId, now, BookAsOf, "own.pdf");
        var otherBookId = await InsertBookExecutionAsync(
            tenant.Id, otherAdviser.PublicId, now.AddMinutes(-1), BookAsOf, "other.pdf");
        var bookWithoutAdviserId = await InsertRawExecutionAsync(
            tenant.Id,
            "adviser-balances",
            """{"includeClosedAccounts":false}""",
            now.AddMinutes(-2));
        var unreadableId = await InsertRawExecutionAsync(
            tenant.Id,
            "customer-transactions",
            "not-json",
            now.AddMinutes(-3));

        var adminIds = await ReadIdsAsync(admin.AccessToken);
        adminIds.ShouldBe(
            [assignedId, unassignedId, ownBookId, otherBookId, bookWithoutAdviserId],
            ignoreOrder: true);
        adminIds.ShouldNotContain(unreadableId);

        var customerScope = await ReadIdsAsync(admin.AccessToken, "?scope=Customer");
        customerScope.ShouldBe([assignedId, unassignedId], ignoreOrder: true);
        var bookScope = await ReadIdsAsync(admin.AccessToken, "?scope=Book");
        bookScope.ShouldBe([ownBookId, otherBookId, bookWithoutAdviserId], ignoreOrder: true);

        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);
        (await ReadIdsAsync(adviserTokens.AccessToken)).ShouldBe([assignedId, ownBookId], ignoreOrder: true);
        (await ReadIdsAsync(adviserTokens.AccessToken, "?scope=Customer")).ShouldBe([assignedId]);
        (await ReadIdsAsync(adviserTokens.AccessToken, "?scope=Book")).ShouldBe([ownBookId]);

        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        (await ReadIdsAsync(customerTokens.AccessToken)).ShouldBe([assignedId]);
        var otherTokens = await TestApp.IssueAccessTokenAsync(otherCustomer);
        (await ReadIdsAsync(otherTokens.AccessToken)).ShouldBe([unassignedId]);

        (await GetAsync(customerTokens.AccessToken, "?scope=Book")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(admin.AccessToken, "?scope=Nope")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var system = await TenantHttp.SignInSystemAdmin();
        (await GetAsync(system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task List_PagesAfterScopeFilter()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Execution Page", "firm-execution-page");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-execution-page", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-execution-page", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-execution-page", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-execution-page", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-execution-page", "Password1!", tenant.Id, otherAdviser.Id);

        var oldest = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = new List<Guid>();
        for (var minute = 0; minute < 21; minute++)
        {
            ids.Add(await InsertCustomerExecutionAsync(tenant.Id, customer.PublicId, oldest.AddMinutes(minute)));
        }

        var hidden = await InsertCustomerExecutionAsync(tenant.Id, otherCustomer.PublicId, oldest.AddMinutes(10));
        var newestFirst = ids.AsEnumerable().Reverse().ToList();
        var adviserTokens = await TestApp.IssueAccessTokenAsync(adviser);

        var first = await ReadPageAsync(adviserTokens.AccessToken);
        first.Page.ShouldBe(1);
        first.PageSize.ShouldBe(20);
        first.TotalCount.ShouldBe(21);
        first.Ids.ShouldBe(newestFirst.Take(20).ToList());
        first.Ids.ShouldNotContain(hidden);

        var second = await ReadPageAsync(adviserTokens.AccessToken, "?page=2");
        second.Page.ShouldBe(2);
        second.PageSize.ShouldBe(20);
        second.TotalCount.ShouldBe(21);
        second.Ids.ShouldBe([ids[0]]);

        var all = await ReadPageAsync(adviserTokens.AccessToken, "?pageSize=100");
        all.Page.ShouldBe(1);
        all.PageSize.ShouldBe(100);
        all.TotalCount.ShouldBe(21);
        all.Ids.ShouldBe(newestFirst);

        var past = await ReadPageAsync(adviserTokens.AccessToken, "?page=3");
        past.TotalCount.ShouldBe(21);
        past.Ids.ShouldBeEmpty();

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-execution-page", "Password1!", "firm-execution-page");
        (await ReadPageAsync(admin.AccessToken)).TotalCount.ShouldBe(22);

        (await GetAsync(adviserTokens.AccessToken, "?page=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(adviserTokens.AccessToken, "?pageSize=101")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<(int Page, int PageSize, int TotalCount, List<Guid> Ids)> ReadPageAsync(
        string accessToken,
        string query = "")
    {
        var response = await GetAsync(accessToken, query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return (
            root.GetProperty("page").GetInt32(),
            root.GetProperty("pageSize").GetInt32(),
            root.GetProperty("totalCount").GetInt32(),
            root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList());
    }

    private static Task<Guid> InsertCustomerExecutionAsync(int tenantId, Guid customerId, DateTimeOffset created)
    {
        var parameters = $$"""{"customerId":"{{customerId}}","from":"2026-01-01","to":"2026-03-31","includeClosedAccounts":false}""";
        return InsertRawExecutionAsync(tenantId, "customer-transactions", parameters, created);
    }

    private static async Task<List<Guid>> ReadIdsAsync(string accessToken, string query = "")
    {
        var response = await GetAsync(accessToken, query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
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
