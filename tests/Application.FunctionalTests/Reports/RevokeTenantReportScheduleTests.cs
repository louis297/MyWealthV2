using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class RevokeTenantReportScheduleTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task Revoke_RemovesOnlyThatTenantsJobsForThatReport_AndKeepsTheRows()
    {
        var north = await TestApp.CreateTenantAsync("North Revoke Schedule", "north-revoke-schedule");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@north-revoke-schedule", "Password1!", north.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@north-revoke-schedule", "Password1!", north.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@north-revoke-schedule", "Password1!", north.Id, adviser.Id);
        var south = await TestApp.CreateTenantAsync("South Revoke Schedule", "south-revoke-schedule");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Sue", "sue@south-revoke-schedule", "Password1!", south.Id);
        var southAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Sol", "sol@south-revoke-schedule", "Password1!", south.Id);
        var southCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Sam", "sam@south-revoke-schedule", "Password1!", south.Id, southAdviser.Id);
        await GrantAsync(north.PublicId, CustomerTransactionsId);
        await GrantAsync(north.PublicId, AdviserBalancesId);
        await GrantAsync(south.PublicId, CustomerTransactionsId);

        var northAdmin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@north-revoke-schedule", "Password1!", "north-revoke-schedule");
        var first = await CreateAsync(northAdmin.AccessToken, customer.PublicId);
        var second = await CreateAsync(northAdmin.AccessToken, customer.PublicId);
        var southAdmin = await FunctionalTestSetup.Oidc.SignInAsync(
            "sue@south-revoke-schedule", "Password1!", "south-revoke-schedule");
        var southId = await CreateAsync(southAdmin.AccessToken, southCustomer.PublicId);
        var balancesId = await InsertScheduleAsync(north.Id, AdviserBalancesId);

        var system = await TenantHttp.SignInSystemAdmin();
        var revoked = await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{north.PublicId}/reports/{CustomerTransactionsId}",
            system.AccessToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var scheduler = FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>();
        scheduler.Removed.OrderBy(id => id).ToList()
            .ShouldBe(new[] { first, second }.OrderBy(id => id).ToList());

        var again = await TenantHttp.Send(
            HttpMethod.Delete,
            $"/tenants/{north.PublicId}/reports/{AdviserBalancesId}",
            system.AccessToken);
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        scheduler.Removed.OrderBy(id => id).ToList()
            .ShouldBe(new[] { first, second, balancesId }.OrderBy(id => id).ToList());

        var remaining = await ScheduleIdsAsync();
        remaining.OrderBy(id => id).ToList()
            .ShouldBe(new[] { first, second, southId, balancesId }.OrderBy(id => id).ToList());
        (await IsActiveAsync(first)).ShouldBeTrue();
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid customerId)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId, from = "2026-01-01", to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive = true
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
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

    private static async Task<List<Guid>> ScheduleIdsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportSchedules.AsNoTracking().Select(row => row.PublicId).ToListAsync();
    }

    private static async Task<bool> IsActiveAsync(Guid publicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportSchedules.AsNoTracking()
            .Where(row => row.PublicId == publicId)
            .Select(row => row.IsActive)
            .SingleAsync();
    }

    private static async Task GrantAsync(Guid tenantId, Guid reportId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            system.AccessToken,
            new { reportId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
