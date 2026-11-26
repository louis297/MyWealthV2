using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Valuation-history R16, R20: the optional <c>date</c> of the Adviser Balances Report on a request, its
/// refusals, and its refusal on a schedule.</summary>
[Parallelizable]
public class AdviserBalancesDateTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task ARequestWithADate_IsQueued_AndTheExecutionKeepsTheDate()
    {
        var (admin, adviser, _) = await FirmAsync();

        var created = await RequestAsync(admin, new { adviserId = adviser, date = "2026-03-11" });

        created.StatusCode.ShouldBe(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync());
        var executions = await TenantHttp.Send(HttpMethod.Get, "/reports/executions?scope=Book", admin);
        using var json = JsonDocument.Parse(await executions.Content.ReadAsStringAsync());
        var parameters = json.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("parameters");
        parameters.TryGetProperty("date", out var date).ShouldBeTrue(parameters.GetRawText());
        date.GetString().ShouldBe("2026-03-11");
        parameters.GetProperty("includeClosedAccounts").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task ADateThatIsNotAllowed_Is400OnDate()
    {
        var (admin, adviser, _) = await FirmAsync();
        var tooLate = ValuationRunDb.LastFinalDay.AddDays(1).ToString("yyyy-MM-dd");

        foreach (var (date, message) in new[]
                 {
                     ("2026-3-11", "date must be a date as yyyy-MM-dd."),
                     (tooLate, "A report for a past date can be made up to the day before yesterday (UTC).")
                 })
        {
            var errors = await CustodialHttp.ErrorsAsync(await RequestAsync(admin, new { adviserId = adviser, date }));
            errors.Keys.ShouldBe(["date"]);
            errors["date"].ShouldBe([message]);
        }

        var closed = await CustodialHttp.ErrorsAsync(
            await RequestAsync(admin, new { adviserId = adviser, date = "2026-03-11", includeClosedAccounts = true }));
        closed.Keys.ShouldBe(["includeClosedAccounts"]);
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued.ShouldBeEmpty();
    }

    [Test]
    public async Task ADateOfAnotherFirmsAdviser_Is404AsToday()
    {
        var (admin, _, southAdviser) = await FirmAsync();

        (await RequestAsync(admin, new { adviserId = southAdviser, date = "2026-03-11" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AScheduleWithADate_Is400_OnCreateAndOnUpdate()
    {
        var (admin, adviser, _) = await FirmAsync();

        var create = await CustodialHttp.ErrorsAsync(await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", admin, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId = adviser, date = "2026-03-11" },
            cron = "0 9 1 * *",
            isActive = true
        }));
        create.Keys.ShouldBe(["date"]);
        create["date"].ShouldBe(["A schedule reports the balances at the time it runs."]);

        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", admin, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId = adviser },
            cron = "0 9 1 * *",
            isActive = true
        });
        var scheduleId = await CustodialHttp.ReadIdAsync(created);
        var list = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules", admin);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scheduleId)
            .GetProperty("rowVersion").GetString();

        var update = await CustodialHttp.ErrorsAsync(await TenantHttp.Send(HttpMethod.Put, $"/reports/schedules/{scheduleId}", admin, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId = adviser, date = "2026-03-11" },
            cron = "0 9 1 * *",
            isActive = true,
            rowVersion
        }));
        update.Keys.ShouldBe(["date"]);
    }

    private static Task<HttpResponseMessage> RequestAsync(string accessToken, object parameters) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/request", accessToken, new { reportId = AdviserBalancesId, parameters });

    /// <summary>A firm with the report granted, its TenantAdmin's token, its Adviser, and another firm's Adviser.</summary>
    private static async Task<(string Admin, Guid Adviser, Guid SouthAdviser)> FirmAsync()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Dated", "firm-dated");
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@firm-dated", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firm-dated", "Password1!", tenant.Id);
        var south = await TestApp.CreateTenantAsync("South Dated", "south-dated");
        var southAdviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Sol", "sol@south-dated", "Password1!", south.Id);
        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenant.PublicId}/reports", system.AccessToken, new { reportId = AdviserBalancesId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-dated", "Password1!", "firm-dated");
        return (admin.AccessToken, adviser.PublicId, southAdviser.PublicId);
    }
}
