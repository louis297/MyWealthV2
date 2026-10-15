using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class AdviserBalancesScopeTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task Adviser_SeesAndChangesOnlyTheirOwnBook_AndCannotDownloadAnother()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Book Scope", "firm-book-scope");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-book-scope", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-book-scope", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-book-scope", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", "cee@firm-book-scope", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);

        var admin = await FunctionalTestSetup.Oidc.SignInAsync(
            "tess@firm-book-scope", "Password1!", "firm-book-scope");
        var ownScheduleId = await CreateScheduleAsync(admin.AccessToken, adviser.PublicId, "0 9 1 * *");
        var otherScheduleId = await CreateScheduleAsync(admin.AccessToken, otherAdviser.PublicId, "0 8 1 * *");

        var adminItems = await ListAsync(admin.AccessToken);
        adminItems.Select(item => item.Id).ShouldBe([ownScheduleId, otherScheduleId]);

        var adviserTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "ann@firm-book-scope", "Password1!", "firm-book-scope");
        var ownItems = await ListAsync(adviserTokens.AccessToken);
        ownItems.Select(item => item.Id).ShouldBe([ownScheduleId]);
        ownItems[0].AdviserId.ShouldBe(adviser.PublicId);

        var otherTokens = await FunctionalTestSetup.Oidc.SignInAsync(
            "bea@firm-book-scope", "Password1!", "firm-book-scope");
        (await ListAsync(otherTokens.AccessToken)).Select(item => item.Id).ShouldBe([otherScheduleId]);

        (await UpdateAsync(adviserTokens.AccessToken, ownScheduleId, adviser.PublicId, "15 8 * * 1", ownItems[0].RowVersion))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var otherVersion = adminItems.Single(item => item.Id == otherScheduleId).RowVersion;
        (await UpdateAsync(
            adviserTokens.AccessToken,
            otherScheduleId,
            adviser.PublicId,
            "15 8 * * 1",
            otherVersion)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var afterUpdate = await ListAsync(admin.AccessToken);
        afterUpdate.Single(item => item.Id == ownScheduleId).Cron.ShouldBe("15 8 * * 1");
        afterUpdate.Single(item => item.Id == otherScheduleId).Cron.ShouldBe("0 8 1 * *");

        (await DisableAsync(adviserTokens.AccessToken, otherScheduleId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await DisableAsync(adviserTokens.AccessToken, ownScheduleId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDisable = await ListAsync(admin.AccessToken);
        afterDisable.Single(item => item.Id == ownScheduleId).IsActive.ShouldBeFalse();
        afterDisable.Single(item => item.Id == otherScheduleId).IsActive.ShouldBeTrue();

        var ownExecution = await RequestAndExecuteAsync(admin.AccessToken, adviser.PublicId);
        var otherExecution = await RequestAndExecuteAsync(admin.AccessToken, otherAdviser.PublicId);
        (await DownloadAsync(adviserTokens.AccessToken, ownExecution)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DownloadAsync(adviserTokens.AccessToken, otherExecution)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await DownloadAsync(admin.AccessToken, ownExecution)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DownloadAsync(admin.AccessToken, otherExecution)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var customerTokens = await TestApp.IssueAccessTokenAsync(customer);
        (await DownloadAsync(customerTokens.AccessToken, ownExecution)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> CreateScheduleAsync(string accessToken, Guid adviserId, string cron)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", accessToken, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId },
            cron,
            isActive = true
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<ScheduleItem>> ListAsync(string accessToken)
    {
        var response = await TenantHttp.Send(
            HttpMethod.Get,
            "/reports/schedules?scope=Book",
            accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(item =>
        {
            var parameters = item.GetProperty("parameters");
            return new ScheduleItem(
                item.GetProperty("id").GetGuid(),
                parameters.GetProperty("adviserId").GetGuid(),
                item.GetProperty("cron").GetString()!,
                item.GetProperty("isActive").GetBoolean(),
                item.GetProperty("rowVersion").GetString()!);
        }).ToList();
    }

    private static Task<HttpResponseMessage> UpdateAsync(
        string accessToken,
        Guid id,
        Guid adviserId,
        string cron,
        string rowVersion) =>
        TenantHttp.Send(HttpMethod.Put, $"/reports/schedules/{id}", accessToken, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId },
            cron,
            isActive = true,
            rowVersion
        });

    private static Task<HttpResponseMessage> DisableAsync(string accessToken, Guid id) =>
        TenantHttp.Send(HttpMethod.Post, $"/reports/schedules/{id}/disable", accessToken);

    private static async Task<Guid> RequestAndExecuteAsync(string accessToken, Guid adviserId)
    {
        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            accessToken,
            new { reportId = AdviserBalancesId, parameters = new { adviserId } });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        return executionId;
    }

    private static Task<HttpResponseMessage> DownloadAsync(string? accessToken, Guid executionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/reports/executions/{executionId}/download");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return FunctionalTestSetup.WebClient.SendAsync(request);
    }

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

    private sealed record ScheduleItem(Guid Id, Guid AdviserId, string Cron, bool IsActive, string RowVersion);
}
