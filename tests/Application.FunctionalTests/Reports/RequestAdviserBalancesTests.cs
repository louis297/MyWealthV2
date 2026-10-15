using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RequestAdviserBalancesTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task ManualRequest_SetsAsOf_AndRejectsAMissingOrForeignAdviser()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Balances", "firm-balances");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-balances", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-balances", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-balances", "Password1!", tenant.Id);
        var south = await TestApp.CreateTenantAsync("South Balances", "south-balances");
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-balances", "Password1!", south.Id);
        await GrantAsync(tenant.PublicId);

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-balances", "Password1!", "firm-balances");
        var before = DateTimeOffset.UtcNow;
        var created = await PostAsync(adviserTokens.AccessToken, new { });
        var after = DateTimeOffset.UtcNow;

        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        var row = await ReadExecutionAsync(executionId);
        row.TriggerKind.ShouldBe("Manual");
        row.Status.ShouldBe("Queued");
        row.AsOf.ShouldNotBeNull();
        row.AsOf!.Value.Offset.ShouldBe(TimeSpan.Zero);
        row.AsOf.Value.ShouldBeGreaterThanOrEqualTo(before);
        row.AsOf.Value.ShouldBeLessThanOrEqualTo(after);
        row.Parameters.ShouldBe(
            $$"""{"adviserId":"{{adviser.PublicId}}","includeClosedAccounts":false}""");

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-balances", "Password1!", "firm-balances");
        var missing = await PostAsync(admin.AccessToken, new { });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("adviserId");

        var clientAsOf = await PostAsync(adviserTokens.AccessToken, new
        {
            adviserId = adviser.PublicId,
            asOf = "2026-10-16T08:30:00Z"
        });
        clientAsOf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await clientAsOf.Content.ReadAsStringAsync()).ShouldContain("unknown");

        (await PostAsync(adviserTokens.AccessToken, new { adviserId = otherAdviser.PublicId }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(admin.AccessToken, new { adviserId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(admin.AccessToken, new { adviserId = southAdviser.PublicId }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var forAdviser = await PostAsync(admin.AccessToken, new { adviserId = adviser.PublicId });
        forAdviser.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var adminJson = JsonDocument.Parse(await forAdviser.Content.ReadAsStringAsync());
        var adminExecutionId = adminJson.RootElement.GetProperty("id").GetGuid();
        var adminRow = await ReadExecutionAsync(adminExecutionId);
        adminRow.AsOf.ShouldNotBeNull();
        adminRow.Parameters.ShouldBe(
            $$"""{"adviserId":"{{adviser.PublicId}}","includeClosedAccounts":false}""");

        var explicitSelf = await PostAsync(adviserTokens.AccessToken, new { adviserId = adviser.PublicId });
        explicitSelf.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var selfJson = JsonDocument.Parse(await explicitSelf.Content.ReadAsStringAsync());
        var selfExecutionId = selfJson.RootElement.GetProperty("id").GetGuid();

        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued
            .ShouldBe([executionId, adminExecutionId, selfExecutionId]);
    }

    private static Task<HttpResponseMessage> PostAsync(string accessToken, object parameters) =>
        TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            accessToken,
            new { reportId = AdviserBalancesId, parameters });

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
