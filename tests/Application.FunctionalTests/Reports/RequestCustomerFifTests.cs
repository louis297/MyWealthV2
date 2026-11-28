using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.CorporateActions;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Fif-report R2 to R8, §8: <c>POST /reports/request</c> for the report <c>customer-fif</c>.</summary>
[Parallelizable]
public class RequestCustomerFifTests : TestBase
{
    private static readonly Guid FifId = Guid.Parse("7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44");

    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private const string OverseasMessage = "A FIF report is made for a customer whose tax residency is New Zealand.";

    [SetUp]
    public void ClearScheduler()
    {
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Clear();
    }

    [Test]
    public async Task WithoutBearer_Is401WithoutRedirect()
    {
        var response = await PostAsync(null, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TenantAdminAssignedAdviserAndTheCustomer_QueueAnExecutionWithTheSnapshot()
    {
        var firm = await FirmAsync("fif-queue");
        var callers = new[]
        {
            await TestApp.IssueAccessTokenAsync(firm.Admin),
            await TestApp.IssueAccessTokenAsync(firm.Adviser),
            await TestApp.IssueAccessTokenAsync(firm.Customer)
        };

        foreach (var caller in callers)
        {
            var response = await PostAsync(caller.AccessToken, firm.Customer.PublicId);
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var executionId = json.RootElement.GetProperty("id").GetGuid();

            var row = await ReadExecutionAsync(executionId);
            row.TriggerKind.ShouldBe("Manual");
            row.Status.ShouldBe("Queued");
            row.AsOf.ShouldBeNull();
            row.Parameters.ShouldBe($$"""{"customerId":"{{firm.Customer.PublicId}}","taxYear":2026}""");
            FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingReportScheduler>().Enqueued
                .ShouldContain(executionId);
        }
    }

    [Test]
    public async Task ACustomerWithNoSavedTaxProfile_IsReadAsNewZealand()
    {
        var firm = await FirmAsync("fif-noprofile");
        var admin = await TestApp.IssueAccessTokenAsync(firm.Admin);

        (await PostAsync(admin.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await TaxProfileHttp.SetAsync(admin.AccessToken, firm.Customer.PublicId, taxResidency: "NewZealand");
        (await PostAsync(admin.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task SystemAdmin_Is403()
    {
        var firm = await FirmAsync("fif-system");
        var system = await TenantHttp.SignInSystemAdmin();

        (await PostAsync(system.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnUnassignedAdviserAnotherCustomerAndAnUnknownId_Are404()
    {
        var firm = await FirmAsync("fif-scope");
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@fif-scope", "Password1!", firm.Tenant.Id);
        var unassigned = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@fif-scope", "Password1!", firm.Tenant.Id, otherAdviser.Id);
        var adviser = await TestApp.IssueAccessTokenAsync(firm.Adviser);
        var customer = await TestApp.IssueAccessTokenAsync(firm.Customer);
        var admin = await TestApp.IssueAccessTokenAsync(firm.Admin);

        (await PostAsync(adviser.AccessToken, unassigned.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(customer.AccessToken, unassigned.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(admin.AccessToken, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(admin.AccessToken, firm.Adviser.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheReportNotGranted_Is404()
    {
        var firm = await FirmAsync("fif-ungranted", grant: false);
        var admin = await TestApp.IssueAccessTokenAsync(firm.Admin);

        (await PostAsync(admin.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EachBadParameter_Is400AtItsField()
    {
        var firm = await FirmAsync("fif-invalid");
        var admin = (await TestApp.IssueAccessTokenAsync(firm.Admin)).AccessToken;
        var customerId = firm.Customer.PublicId;
        var inProgress = TaxYear.Of(DateTimeOffset.UtcNow);

        (await ErrorsAsync(admin, new { taxYear = 2026 }))
            .ShouldBeError("customerId", "customerId is required.");
        (await ErrorsAsync(admin, new { customerId }))
            .ShouldBeError("taxYear", "taxYear is required.");
        (await ErrorsAsync(admin, new { customerId, taxYear = "2026" }))
            .ShouldBeError("parameters", "parameters are not valid.");
        (await ErrorsAsync(admin, new { customerId, taxYear = 2026.5 }))
            .ShouldBeError("parameters", "parameters are not valid.");
        (await ErrorsAsync(admin, new { customerId, taxYear = 2026, from = "2025-04-01" }))
            .ShouldBeError("parameters", "unknown field.");
        (await ErrorsAsync(admin, new { customerId, taxYear = 2008 }))
            .ShouldBeError("taxYear", "taxYear must be 2009 or later.");
        (await ErrorsAsync(admin, new { customerId, taxYear = inProgress }))
            .ShouldBeError("taxYear", $"Tax year {inProgress} can be reported from 2 April {inProgress}.");
    }

    [Test]
    public async Task AnOverseasCustomer_Is400AtCustomerId_AndACallerOutOfScopeStillGets404()
    {
        var firm = await FirmAsync("fif-overseas");
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@fif-overseas", "Password1!", firm.Tenant.Id);
        var other = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@fif-overseas", "Password1!", firm.Tenant.Id, otherAdviser.Id);
        var admin = await TestApp.IssueAccessTokenAsync(firm.Admin);
        await TaxProfileHttp.SetAsync(admin.AccessToken, firm.Customer.PublicId, taxResidency: "Overseas", rwtRatePercent: null);

        foreach (var caller in new[] { firm.Admin, firm.Adviser, firm.Customer })
        {
            var token = (await TestApp.IssueAccessTokenAsync(caller)).AccessToken;
            (await CorporateActionHttp.ErrorsAsync(await PostAsync(token, firm.Customer.PublicId)))
                .ShouldBeError("customerId", OverseasMessage);
        }

        var outOfScope = await TestApp.IssueAccessTokenAsync(otherAdviser);
        (await PostAsync(outOfScope.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherCustomer = await TestApp.IssueAccessTokenAsync(other);
        (await PostAsync(otherCustomer.AccessToken, firm.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ExecutionCountAsync(firm.Tenant.Id)).ShouldBe(0);
    }

    [Test]
    public async Task AScheduleThatNamesTheReport_Is404_AlsoAfterScheduledWasAddedToTheCatalogRow()
    {
        var firm = await FirmAsync("fif-schedule");
        await GrantAsync(firm.Tenant.PublicId, CustomerTransactionsId);
        var admin = (await TestApp.IssueAccessTokenAsync(firm.Admin)).AccessToken;
        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", admin, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = firm.Customer.PublicId, from = "2026-01-01", to = "2026-03-31" },
            cron = "0 9 1 * *",
            isActive = true
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var scheduleId = createdJson.RootElement.GetProperty("id").GetGuid();

        await AssertSchedulesRefusedAsync(admin, firm.Customer.PublicId, scheduleId);

        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(HttpMethod.Put, $"/reports/{FifId}", system.AccessToken,
            new { allowedTriggers = new[] { "manual", "scheduled" } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AssertSchedulesRefusedAsync(admin, firm.Customer.PublicId, scheduleId);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsCustomerIs404_AndItsExecutionsAreInNoList()
    {
        var north = await FirmAsync("fif-north");
        var south = await FirmAsync("fif-south");
        var northAdmin = (await TestApp.IssueAccessTokenAsync(north.Admin)).AccessToken;
        var southAdmin = (await TestApp.IssueAccessTokenAsync(south.Admin)).AccessToken;
        var northAdviser = (await TestApp.IssueAccessTokenAsync(north.Adviser)).AccessToken;
        var northCustomer = (await TestApp.IssueAccessTokenAsync(north.Customer)).AccessToken;

        (await PostAsync(northAdmin, south.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(northAdviser, south.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(northCustomer, south.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var southRequest = await PostAsync(southAdmin, south.Customer.PublicId);
        southRequest.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var southJson = JsonDocument.Parse(await southRequest.Content.ReadAsStringAsync());
        var southExecution = southJson.RootElement.GetProperty("id").GetGuid();
        (await PostAsync(northAdmin, north.Customer.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        foreach (var token in new[] { northAdmin, northAdviser, northCustomer })
        {
            var list = await TenantHttp.Send(HttpMethod.Get, "/reports/executions?scope=Customer", token);
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var ids = json.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()).ToList();
            ids.Count.ShouldBe(1);
            ids.ShouldNotContain(southExecution);
            (await TenantHttp.Send(HttpMethod.Get, $"/reports/executions/{southExecution}", token))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    private static async Task AssertSchedulesRefusedAsync(string admin, Guid customerId, Guid scheduleId)
    {
        var parameters = new { customerId, taxYear = 2026 };
        (await TenantHttp.Send(HttpMethod.Post, "/reports/schedules", admin, new
        {
            reportId = FifId,
            parameters,
            cron = "0 9 1 * *",
            isActive = true
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules?scope=Customer", admin);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var schedule = json.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == scheduleId);
        (await TenantHttp.Send(HttpMethod.Put, $"/reports/schedules/{scheduleId}", admin, new
        {
            reportId = FifId,
            parameters,
            cron = "0 9 1 * *",
            isActive = true,
            rowVersion = schedule.GetProperty("rowVersion").GetString()
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record Firm(Tenant Tenant, User Admin, User Adviser, User Customer);

    private static async Task<Firm> FirmAsync(string code, bool grant = true)
    {
        var tenant = await TestApp.CreateTenantAsync($"Firm {code}", code);
        var admin = await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", $"tess@{code}", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", $"ann@{code}", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee", $"cee@{code}", "Password1!", tenant.Id, adviser.Id);
        if (grant)
            await GrantAsync(tenant.PublicId, FifId);
        return new Firm(tenant, admin, adviser, customer);
    }

    private static Task<HttpResponseMessage> PostAsync(string? accessToken, Guid customerId) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/request", accessToken, new
        {
            reportId = FifId,
            parameters = new { customerId, taxYear = 2026 }
        });

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(string accessToken, object parameters) =>
        await CorporateActionHttp.ErrorsAsync(await TenantHttp.Send(
            HttpMethod.Post, "/reports/request", accessToken, new { reportId = FifId, parameters }));

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

    private static async Task<int> ExecutionCountAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReportExecutions.IgnoreQueryFilters().CountAsync(row => row.TenantId == tenantId);
    }

    private static async Task<(string TriggerKind, string Status, DateTimeOffset? AsOf, string Parameters)> ReadExecutionAsync(
        Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(execution => execution.PublicId == executionId);
        return (row.TriggerKind, row.Status, row.AsOf, row.ParametersJson);
    }
}

internal static class RequestErrorAssertions
{
    /// <summary>The 400 names one field, with one message.</summary>
    public static void ShouldBeError(this Dictionary<string, string[]> errors, string key, string message)
    {
        errors.Keys.ShouldBe([key]);
        errors[key].ShouldBe([message]);
    }
}
