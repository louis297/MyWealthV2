using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RequestCustomerTransactionsTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task TenantAdmin_QueuesAnExecution_AndAdviserCannotTargetAnUnassignedCustomer()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Queue", "firm-queue");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-queue", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-queue", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-queue", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-queue", "Password1!", tenant.Id, adviser.Id);
        var south = await TestApp.CreateTenantAsync("South Queue", "south-queue");
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-queue", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-queue", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(tenant.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-queue", "Password1!", "firm-queue");
        var created = await PostAsync(admin.AccessToken, customer.PublicId);
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();

        var row = await ReadExecutionAsync(executionId);
        row.TriggerKind.ShouldBe("Manual");
        row.Status.ShouldBe("Queued");
        row.AsOf.ShouldBeNull();
        row.Parameters.ShouldBe(
            $$"""{"customerId":"{{customer.PublicId}}","from":"2026-01-01","to":"2026-03-31","includeClosedAccounts":false}""");
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued
            .ShouldBe([executionId]);

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-queue", "Password1!", "firm-queue");
        var unassigned = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firm-queue", "Password1!", tenant.Id, otherAdviser.Id);
        (await PostAsync(adviserTokens.AccessToken, unassigned.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(adviserTokens.AccessToken, southCustomer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(adviserTokens.AccessToken, customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private static object Body(Guid customerId) => new
    {
        reportId = CustomerTransactionsId,
        parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" }
    };

    private static Task<HttpResponseMessage> PostAsync(string accessToken, Guid customerId) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/request", accessToken, Body(customerId));

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

    private static async Task<(string TriggerKind, string Status, DateTimeOffset? AsOf, string Parameters)> ReadExecutionAsync(
        Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT [TriggerKind], [Status], [AsOf], [ParametersJson]
            FROM [ReportExecutions]
            WHERE [PublicId] = @id
            """;
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = executionId;
        command.Parameters.Add(id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetString(3));
    }
}
