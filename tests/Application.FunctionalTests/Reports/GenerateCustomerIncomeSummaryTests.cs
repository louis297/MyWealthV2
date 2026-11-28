using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.TaxYears;
using MyWealthV2.Application.Reports;
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Income-summary R20 to R22, §10: the worked example through the API, then the job, and the stored PDF.</summary>
[Parallelizable]
public class GenerateCustomerIncomeSummaryTests : TestBase
{
    private static readonly Guid ReportId = Guid.Parse("4b6e2d91-3f7a-4c18-9d52-8a1e7c0b6f55");

    [Test]
    public async Task TheWorkedExample_Succeeds_WithNoAsOf_AndThePdfHoldsItsFigures()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        await GrantAsync(world.Base.Tenant.PublicId);

        var executionId = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        row.AsOf.ShouldBeNull();
        row.ResultStorageKey.ShouldBe($"{world.Base.Tenant.PublicId:D}/{executionId:D}.pdf");
        var text = Text(await ReadFileAsync(world.Base.Tenant.PublicId, executionId));
        text.ShouldContain("Tax Year Income Summary");
        text.ShouldContain($"Customer: {world.Base.Customer.Name}");
        text.ShouldContain("Tenant: North Advisory");
        text.ShouldContain("Tax year: 2026 (1 April 2025 to 31 March 2026, New Zealand time)");
        text.ShouldContain("Status: Complete");
        text.ShouldContain("Tax residency on file: New Zealand");
        foreach (var figure in new[] { "1164.95", "1444.95", "280.00", "66.74", "71.23", "133.70", "25.51", "96.74", "49.05", "7.05", "2.32", "117.95" })
            text.ShouldContain(figure);
        text.ShouldContain("Interest charged: 8.00. It is not taken off the interest received.");
        text.ShouldContain("x 1.72 (2026-01-30)");
        text.ShouldContain("Managed");
        text.ShouldContain("Held elsewhere");
    }

    [Test]
    public async Task APaymentBackdatedAfterTheFirstReport_LeavesItsFileAsItIs_AndASecondRequestGivesAnotherFigure()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        await GrantAsync(world.Base.Tenant.PublicId);
        var first = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(first);
        var firstBytes = await ReadFileAsync(world.Base.Tenant.PublicId, first);

        await IncomeSummaryWorld.HandInterestAsync(world.Token, world.Base.A2, 50.00m, "2025-08-01T00:00:00Z");
        var second = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(second);

        (await ReadFileAsync(world.Base.Tenant.PublicId, first)).ShouldBe(firstBytes);
        var firstText = Text(firstBytes);
        firstText.ShouldContain("133.70");
        firstText.ShouldNotContain("183.70");
        Text(await ReadFileAsync(world.Base.Tenant.PublicId, second)).ShouldContain("183.70");
    }

    [Test]
    public async Task APaymentWithNoRate_Succeeds_AndThePdfSaysIncomplete()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        await GrantAsync(world.Base.Tenant.PublicId);
        // 2025-07-20: the latest USD row is that of 2025-05-20, older than 4 days.
        await PostInterestAsync(world.Base.A1, 2.00m, 33m, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30), IncomeSummaryWorld.Utc(2025, 7, 20, 0, 30));

        var executionId = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        var pages = Pages(await ReadFileAsync(world.Base.Tenant.PublicId, executionId));
        var text = string.Join('\n', pages);
        text.ShouldContain("Status: Incomplete. See section 7.");
        text.ShouldContain("Managed: The interest of 2025-07-20 12:30 could not be converted to New Zealand dollars (Exchange rate too old). It is in no total.");
        pages.ShouldAllBe(page => page.Contains("Incomplete"));
    }

    [Test]
    public async Task AnOverseasCustomer_GetsAPdfWithTheResidencyOnFile()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        await GrantAsync(world.Base.Tenant.PublicId);
        await TaxProfileHttp.SetAsync(world.Token, world.CustomerId, taxResidency: "Overseas", rwtRatePercent: null);

        var executionId = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        var text = Text(await ReadFileAsync(world.Base.Tenant.PublicId, executionId));
        text.ShouldContain("Tax residency on file: Overseas");
        text.ShouldContain("A person who is not a New Zealand tax resident files a different return.");
    }

    [Test]
    public async Task NoPaymentOfAnotherTenantsCustomer_IsInThePdf()
    {
        var world = await IncomeSummaryWorld.CreateAsync();
        await GrantAsync(world.Base.Tenant.PublicId);
        var (south, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin("South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "sam@south.example");
        var southToken = southTokens.AccessToken;
        var southAccount = await AccountHttp.OpenAccountAsync(southToken, southCustomer.PublicId, "South broker", "Brokerage", "NZD");
        await IncomeSummaryWorld.HandInterestAsync(southToken, southAccount, 777.00m, "2025-08-01T00:00:00Z");
        await GrantAsync(south.PublicId);
        await RunAsync(await RequestAsync(southToken, southCustomer.PublicId, 2026));

        var executionId = await RequestAsync(world.Token, world.CustomerId, 2026);
        await RunAsync(executionId);

        var text = Text(await ReadFileAsync(world.Base.Tenant.PublicId, executionId));
        text.ShouldContain("133.70");
        text.ShouldNotContain("777.00");
        text.ShouldNotContain("South broker");
        text.ShouldNotContain("South Advisory");
    }

    private static async Task<Guid> RequestAsync(string token, Guid customerId, int taxYear)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/request", token, new
        {
            reportId = ReportId,
            parameters = new { customerId, taxYear }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task RunAsync(Guid executionId)
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
    }

    private static async Task GrantAsync(Guid tenantId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenantId}/reports", system.AccessToken, new { reportId = ReportId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<byte[]> ReadFileAsync(Guid tenantId, Guid executionId)
    {
        var path = PathOf(tenantId, executionId);
        File.Exists(path).ShouldBeTrue();
        return await File.ReadAllBytesAsync(path);
    }

    private static string PathOf(Guid tenantId, Guid executionId) =>
        Path.Combine(ReportStoragePaths.Root, tenantId.ToString("D"), $"{executionId:D}.pdf");

    private static List<string> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => string.Join(' ', page.GetWords().Select(word => word.Text))).ToList();
    }

    private static string Text(byte[] pdf) => string.Join('\n', Pages(pdf));

    private static async Task<(string Status, DateTimeOffset? AsOf, string? ResultStorageKey, string? ErrorMessage)> ReadExecutionAsync(Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(execution => execution.PublicId == executionId);
        return (row.Status, row.AsOf, row.ResultStorageKey, row.ErrorMessage);
    }
}
