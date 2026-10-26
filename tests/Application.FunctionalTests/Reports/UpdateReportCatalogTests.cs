using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class UpdateReportCatalogTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task OtherRoles_Return403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Catalog", "firm-catalog");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-catalog", "Password1!", tenant.Id);
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-catalog", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-catalog", "Password1!", tenant.Id, adviser.Id);
        var path = $"/reports/{CustomerTransactionsId}";
        var body = new { isActive = false };

        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-catalog", "Password1!", "firm-catalog");
        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@firm-catalog", "Password1!", "firm-catalog");
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);

        (await TenantHttp.Send(HttpMethod.Put, path, admin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Put, path, adviserTokens.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TenantHttp.Send(HttpMethod.Put, path, customerTokens.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UnknownReport_Returns404()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();
        var missing = Guid.NewGuid();

        var response = await TenantHttp.Send(
            HttpMethod.Put,
            $"/reports/{missing}",
            tokens.AccessToken,
            new { isActive = false });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain(missing.ToString());
    }

    [Test]
    public async Task NeitherField_Returns400()
    {
        var tokens = await TenantHttp.SignInSystemAdmin();

        var response = await TenantHttp.Send(
            HttpMethod.Put,
            $"/reports/{CustomerTransactionsId}",
            tokens.AccessToken,
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Disable_RevokesEveryGrant_RemovesHangfire_LeavesRows_EnableRestoresNoGrants()
    {
        var north = await TestApp.CreateTenantAsync("North Catalog", "north-catalog");
        var south = await TestApp.CreateTenantAsync("South Catalog", "south-catalog");
        var tokens = await TenantHttp.SignInSystemAdmin();
        await GrantAsync(tokens.AccessToken, north.PublicId, CustomerTransactionsId);
        await GrantAsync(tokens.AccessToken, south.PublicId, CustomerTransactionsId);
        await GrantAsync(tokens.AccessToken, north.PublicId, AdviserBalancesId);
        var northSchedule = await InsertScheduleAsync(north.Id, CustomerTransactionsId);
        var southSchedule = await InsertScheduleAsync(south.Id, CustomerTransactionsId);
        var balancesSchedule = await InsertScheduleAsync(north.Id, AdviserBalancesId);
        var executionId = await InsertExecutionAsync(north.Id, CustomerTransactionsId);

        try
        {
            var disabled = await TenantHttp.Send(
                HttpMethod.Put,
                $"/reports/{CustomerTransactionsId}",
                tokens.AccessToken,
                new { isActive = false });
            disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await CatalogActiveAsync(CustomerTransactionsId)).ShouldBeFalse();
            (await GrantCountAsync(north.Id, CustomerTransactionsId)).ShouldBe(0);
            (await GrantCountAsync(south.Id, CustomerTransactionsId)).ShouldBe(0);
            (await GrantCountAsync(north.Id, AdviserBalancesId)).ShouldBe(1);
            (await ScheduleActiveAsync(northSchedule)).ShouldBeTrue();
            (await ScheduleActiveAsync(southSchedule)).ShouldBeTrue();
            (await ScheduleActiveAsync(balancesSchedule)).ShouldBeTrue();
            (await ExecutionExistsAsync(executionId)).ShouldBeTrue();

            var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>();
            scheduler.Removed.OrderBy(id => id).ToList()
                .ShouldBe(new[] { northSchedule, southSchedule }.OrderBy(id => id).ToList());

            var again = await TenantHttp.Send(
                HttpMethod.Put,
                $"/reports/{CustomerTransactionsId}",
                tokens.AccessToken,
                new { isActive = false });
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            scheduler.Removed.Count.ShouldBe(2);

            var enabled = await TenantHttp.Send(
                HttpMethod.Put,
                $"/reports/{CustomerTransactionsId}",
                tokens.AccessToken,
                new { isActive = true });
            enabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await CatalogActiveAsync(CustomerTransactionsId)).ShouldBeTrue();
            (await GrantCountAsync(north.Id, CustomerTransactionsId)).ShouldBe(0);
            (await GrantCountAsync(south.Id, CustomerTransactionsId)).ShouldBe(0);
            scheduler.Removed.Count.ShouldBe(2);
        }
        finally
        {
            await SetCatalogActiveAsync(CustomerTransactionsId, active: true);
        }
    }

    private static async Task GrantAsync(string accessToken, Guid tenantId, Guid reportId)
    {
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            accessToken,
            new { reportId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> InsertScheduleAsync(int tenantId, Guid reportPublicId)
    {
        var publicId = Guid.NewGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO [ReportSchedules]
                ([PublicId], [ReportId], [TenantId], [ParametersJson], [IsActive], [Cron], [Created], [CreatedBy])
            SELECT {{publicId}}, [Id], {{tenantId}}, N'{}', 1, N'0 9 1 * *', SYSDATETIMEOFFSET(), N'test'
            FROM [Reports]
            WHERE [PublicId] = {{reportPublicId}}
            """);
        return publicId;
    }

    private static async Task<Guid> InsertExecutionAsync(int tenantId, Guid reportPublicId)
    {
        var publicId = Guid.NewGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO [ReportExecutions]
                ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson], [Created], [CreatedBy])
            SELECT {{publicId}}, [Id], {{tenantId}}, N'Manual', N'Succeeded', N'{}', SYSDATETIMEOFFSET(), N'test'
            FROM [Reports]
            WHERE [PublicId] = {{reportPublicId}}
            """);
        return publicId;
    }

    private static async Task<int> GrantCountAsync(int tenantId, Guid reportPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportId = await db.Reports.AsNoTracking()
            .Where(report => report.PublicId == reportPublicId)
            .Select(report => report.Id)
            .SingleAsync();
        return await db.TenantReports.CountAsync(
            grant => grant.TenantId == tenantId && grant.ReportId == reportId);
    }

    private static async Task<bool> CatalogActiveAsync(Guid reportPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Reports.AsNoTracking()
            .Where(report => report.PublicId == reportPublicId)
            .Select(report => report.IsActive)
            .SingleAsync();
    }

    private static async Task<bool> ScheduleActiveAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportSchedules.AsNoTracking()
            .Where(row => row.PublicId == publicId)
            .Select(row => row.IsActive)
            .SingleAsync();
    }

    private static async Task<bool> ExecutionExistsAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportExecutions.AsNoTracking().AnyAsync(row => row.PublicId == publicId);
    }

    private static async Task SetCatalogActiveAsync(Guid reportPublicId, bool active)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Reports] SET [IsActive] = {active} WHERE [PublicId] = {reportPublicId}");
    }
}
