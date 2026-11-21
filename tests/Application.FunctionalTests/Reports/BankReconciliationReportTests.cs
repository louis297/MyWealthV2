using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.BankReconciliations;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Bank-reconciliation R22–R25: the report scope Tenant and the <c>bank-reconciliation</c> report.</summary>
[Parallelizable]
public class BankReconciliationReportTests : TestBase
{
    private static readonly Guid BankReconciliationId = Guid.Parse("3e7b9d14-6c2a-4f58-b0e1-9a4d2c7f8e33");
    private const string NoRun = "No bank reconciliation has been run for this account.";

    [Test]
    public async Task Request_AsTenantAdmin_RendersTheNewestRunOfTheAccount()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var line = await world.ReceiveAsync(500m, "Unknown ref");
        await BankReconciliationHttp.RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        (await StatementLineHttp.ActAsync(world.TenantAdmin.AccessToken, line.PublicId, "match",
            new() { ["accountId"] = world.AccountId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await BankReconciliationHttp.RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        var execution = await RequestAsync(world.TenantAdmin.AccessToken, new { clientMoneyAccountId = world.ClientMoney.PublicId });
        var text = await ExecuteAndReadAsync(execution, world.Tenant.PublicId);

        text.ShouldContain("Bank reconciliation");
        text.ShouldContain("Tenant: North Advisory");
        text.ShouldContain(world.ClientMoney.AccountNumber);
        text.ShouldContain(world.ClientMoney.BankName);
        text.ShouldContain("Status: Agreed");
        text.ShouldContain("Trigger: Manual");
        text.ShouldContain("Bank balance");
        text.ShouldContain("Custodial cash");
        text.ShouldContain("500.00 USD");
        text.ShouldNotContain("In suspense");
    }

    [Test]
    public async Task Request_WithARun_RendersThatRun_WithItsItemsUnderTheClassHeadings()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        await world.ReceiveAsync(500m, "Unknown ref");
        var first = await BankReconciliationHttp.RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        await BankReconciliationHttp.RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        var execution = await RequestAsync(world.TenantAdmin.AccessToken,
            new { clientMoneyAccountId = world.ClientMoney.PublicId, reconciliationId = first });
        var text = await ExecuteAndReadAsync(execution, world.Tenant.PublicId);

        text.ShouldContain("Status: Breaks");
        text.ShouldContain("Breaks");
        text.ShouldContain("In suspense");
        text.ShouldContain("UnknownReference");
        text.ShouldContain("Difference");
        text.ShouldContain("Reserved cash");
        text.ShouldContain("Payments in flight");
    }

    [Test]
    public async Task Request_ForANoBankBalanceRun_PrintsTheSentenceAndTheCustodialCashOnly()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        await BankReconciliationHttp.RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        var execution = await RequestAsync(world.TenantAdmin.AccessToken, new { clientMoneyAccountId = world.ClientMoney.PublicId });
        var text = await ExecuteAndReadAsync(execution, world.Tenant.PublicId);

        text.ShouldContain("No bank balance yet.");
        text.ShouldContain("Custodial cash");
        text.ShouldNotContain("Bank balance:");
        text.ShouldNotContain("Breaks");
    }

    [Test]
    public async Task Request_WithoutARun_FailsTheExecution_WithItsMessage()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);

        var execution = await RequestAsync(world.TenantAdmin.AccessToken, new { clientMoneyAccountId = world.ClientMoney.PublicId });
        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(execution);
        }

        var row = await ReadExecutionAsync(execution);
        row.Status.ShouldBe("Failed");
        row.ErrorMessage.ShouldBe(NoRun);
    }

    [Test]
    public async Task Request_ForAnotherAccountsRun_OrAnotherTenantsAccount_Returns404_AndWithoutAnAccount400()
    {
        var north = await DepositMatchingWorld.CreateAsync();
        var south = await DepositMatchingWorld.CreateAsync("South Advisory", "south-advisory");
        await GrantAsync(north.Tenant.PublicId);
        var nzd = await BankFeedHttp.ClientMoneyAccountAsync(north.Tenant.Id, "NZD");
        var nzdRun = await BankReconciliationHttp.RunAsync(north.TenantAdmin.AccessToken, nzd.PublicId);
        var southRun = await BankReconciliationHttp.RunAsync(south.TenantAdmin.AccessToken, south.ClientMoney.PublicId);
        var token = north.TenantAdmin.AccessToken;

        (await PostRequestAsync(token, new { clientMoneyAccountId = north.ClientMoney.PublicId, reconciliationId = nzdRun }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostRequestAsync(token, new { clientMoneyAccountId = north.ClientMoney.PublicId, reconciliationId = southRun }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostRequestAsync(token, new { clientMoneyAccountId = south.ClientMoney.PublicId }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostRequestAsync(token, new { clientMoneyAccountId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostRequestAsync(token, new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostRequestAsync(token, new { clientMoneyAccountId = north.ClientMoney.PublicId, tenantId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnAdviserOrACustomer_Gets403_OnTenantScopedRequestsSchedulesAndReads()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var adviser = await world.SignInAdviserAsync();
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);

        foreach (var token in new[] { adviser.AccessToken, customer.AccessToken })
        {
            (await PostRequestAsync(token, new { clientMoneyAccountId = world.ClientMoney.PublicId }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await PostScheduleAsync(token, world.ClientMoney.PublicId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            foreach (var route in new[] { "/reports?scope=Tenant", "/reports/schedules?scope=Tenant", "/reports/executions?scope=Tenant" })
            {
                (await TenantHttp.Send(HttpMethod.Get, route, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
            }
        }

        // An Adviser's catalog without a scope is the two scopes it reads.
        var listed = await TenantHttp.Send(HttpMethod.Get, "/reports", adviser.AccessToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Codes(listed)).ShouldNotContain("bank-reconciliation");
    }

    [Test]
    public async Task ATenantAdmin_ListsTheTenantScope_SchedulesTheReport_AndSeesTheExecution()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var token = world.TenantAdmin.AccessToken;

        var catalog = await TenantHttp.Send(HttpMethod.Get, "/reports?scope=Tenant", token);
        catalog.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Codes(catalog)).ShouldBe(["bank-reconciliation"]);

        var schedule = await PostScheduleAsync(token, world.ClientMoney.PublicId);
        schedule.StatusCode.ShouldBe(HttpStatusCode.Created, await schedule.Content.ReadAsStringAsync());
        var schedules = await TenantHttp.Send(HttpMethod.Get, "/reports/schedules?scope=Tenant", token);
        schedules.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await schedules.Content.ReadAsStringAsync()))
        {
            var item = json.RootElement.EnumerateArray().ShouldHaveSingleItem();
            item.GetProperty("reportId").GetGuid().ShouldBe(BankReconciliationId);
            item.GetProperty("parameters").GetProperty("clientMoneyAccountId").GetGuid().ShouldBe(world.ClientMoney.PublicId);
        }

        var execution = await RequestAsync(token, new { clientMoneyAccountId = world.ClientMoney.PublicId });
        var executions = await TenantHttp.Send(HttpMethod.Get, "/reports/executions?scope=Tenant", token);
        executions.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await executions.Content.ReadAsStringAsync()).ShouldContain(execution.ToString());
        var book = await TenantHttp.Send(HttpMethod.Get, "/reports/executions?scope=Book", token);
        (await book.Content.ReadAsStringAsync()).ShouldNotContain(execution.ToString());
    }

    private static async Task<List<string>> Codes(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement : json.RootElement.GetProperty("items");
        return root.EnumerateArray().Select(item => item.GetProperty("code").GetString()!).ToList();
    }

    private static Task<HttpResponseMessage> PostRequestAsync(string token, object parameters) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/request", token, new { reportId = BankReconciliationId, parameters });

    private static Task<HttpResponseMessage> PostScheduleAsync(string token, Guid clientMoneyAccountId) =>
        TenantHttp.Send(HttpMethod.Post, "/reports/schedules", token, new
        {
            reportId = BankReconciliationId,
            parameters = new { clientMoneyAccountId },
            cron = "15 17 * * *",
            isActive = true
        });

    private static async Task<Guid> RequestAsync(string token, object parameters)
    {
        var response = await PostRequestAsync(token, parameters);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ExecuteAndReadAsync(Guid executionId, Guid tenantId)
    {
        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        }

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        var path = Path.Combine(ReportStoragePaths.Root, tenantId.ToString("D"), $"{executionId:D}.pdf");
        using var document = PdfDocument.Open(await File.ReadAllBytesAsync(path));
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }

    private static async Task<(string Status, string? ErrorMessage)> ReadExecutionAsync(Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.AsNoTracking().SingleAsync(execution => execution.PublicId == executionId);
        return (row.Status, row.ErrorMessage);
    }

    private static async Task GrantAsync(Guid tenantId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(
            HttpMethod.Post,
            $"/tenants/{tenantId}/reports",
            system.AccessToken,
            new { reportId = BankReconciliationId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
