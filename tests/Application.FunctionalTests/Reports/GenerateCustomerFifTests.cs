using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.TaxYears;
using MyWealthV2.Application.Reports;
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Fif-report R8 to R12, §10: the worked example through the API, then the job, and the stored PDF.</summary>
[Parallelizable]
public class GenerateCustomerFifTests : TestBase
{
    private static readonly Guid FifId = Guid.Parse("7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44");

    [Test]
    public async Task TheWorkedExample_Succeeds_WithNoAsOf_AndThePdfHoldsItsFigures()
    {
        var world = await TaxYearWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);

        var executionId = await RequestAsync(world.Token, world.Customer.PublicId, 2026);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        row.AsOf.ShouldBeNull();
        row.ResultStorageKey.ShouldBe($"{world.Tenant.PublicId:D}/{executionId:D}.pdf");
        var text = Text(await ReadFileAsync(world.Tenant.PublicId, executionId));
        text.ShouldContain("FIF Report");
        text.ShouldContain($"Customer: {world.Customer.Name}");
        text.ShouldContain("Tax year: 2026 (1 April 2025 to 31 March 2026, New Zealand time)");
        text.ShouldContain("Status: Complete");
        text.ShouldContain("Tax residency on file: New Zealand");
        foreach (var figure in new[] { "12355.70", "10792.33", "655.42", "1740.87", "13108.48", "13026.08", "117.95", "17.69", "21.23", "52.12884615" })
            text.ShouldContain(figure);
        text.ShouldContain("FIF income, fair dividend rate method: 655.42");
        text.ShouldContain("FIF income, comparative value method: 1740.87");
        text.ShouldContain("2025-04-01 09:15");
        text.ShouldContain("Held elsewhere");
        text.ShouldContain("Managed");
    }

    [Test]
    public async Task AnEntryBackdatedAfterTheFirstReport_LeavesItsFileAsItIs_AndASecondRequestGivesAnotherFigure()
    {
        var world = await TaxYearWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var first = await RequestAsync(world.Token, world.Customer.PublicId, 2026);
        await RunAsync(first);
        var firstBytes = await ReadFileAsync(world.Tenant.PublicId, first);

        await PostAsync(world.Token, Trade(world.A2, "Buy", world.Aaa, 1m, 54.00m, "2025-04-15T00:00:00Z", fxRate: 1.7500m));
        var second = await RequestAsync(world.Token, world.Customer.PublicId, 2026);
        await RunAsync(second);

        (await ReadFileAsync(world.Tenant.PublicId, first)).ShouldBe(firstBytes);
        Text(firstBytes).ShouldContain("FIF income, comparative value method: 1740.87");
        var secondText = Text(await ReadFileAsync(world.Tenant.PublicId, second));
        secondText.ShouldContain("FIF income, comparative value method:");
        secondText.ShouldNotContain("FIF income, comparative value method: 1740.87");
    }

    [Test]
    public async Task AHoldingWithNoClosingPrice_Succeeds_AndThePdfSaysIncompleteWithNoTotals()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin("Firm Unpriced", "firm-unpriced", "tess@unpriced.example",
            adviserEmail: "ann@unpriced.example", customerEmail: "ned@unpriced.example");
        var token = tokens.AccessToken;
        var account = await AccountHttp.OpenAccountAsync(token, customer.PublicId, "Held elsewhere", "Brokerage", "NZD");
        var uuu = await CustodialHttp.CreateInstrumentAsync(token, "UUU", "NZD", price: null);
        await ClassifyAsync(token, uuu, "US", 2025, "Attributing");
        await PostAsync(token, Cash(account, "TransferIn", 10_000.00m, "2024-05-01T00:00:00Z"));
        await PostAsync(token, Trade(account, "Buy", uuu, 100m, 15.00m, "2024-06-03T01:00:00Z"));
        await AddPriceAsync(uuu, "NZD", new DateOnly(2025, 3, 31), 20.00m);
        await GrantAsync(tenant.PublicId);

        var executionId = await RequestAsync(token, customer.PublicId, 2026);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded", row.ErrorMessage);
        var pages = Pages(await ReadFileAsync(tenant.PublicId, executionId));
        var text = string.Join('\n', pages);
        text.ShouldContain("Status: Incomplete. 1 of 1 holdings are affected. See section 9.");
        text.ShouldContain("FIF income totals are not given.");
        text.ShouldNotContain("FIF income, fair dividend rate method:");
        text.ShouldContain("UUU: No market value at the end of the tax year (No price). Its FIF income is not computed.");
        pages.ShouldAllBe(page => page.Contains("Incomplete"));
    }

    [Test]
    public async Task ATaxResidencySetToOverseasBeforeTheJob_FailsWithNoFile()
    {
        var world = await TaxYearWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var executionId = await RequestAsync(world.Token, world.Customer.PublicId, 2026);

        await TaxProfileHttp.SetAsync(world.Token, world.Customer.PublicId, taxResidency: "Overseas", rwtRatePercent: null);
        await RunAsync(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Failed");
        row.ErrorMessage.ShouldBe(CustomerFifBinder.OverseasMessage);
        row.ResultStorageKey.ShouldBeNull();
        File.Exists(PathOf(world.Tenant.PublicId, executionId)).ShouldBeFalse();
    }

    [Test]
    public async Task NothingOfAnotherTenantsCustomer_IsInThePdf()
    {
        var world = await TaxYearWorld.CreateAsync();
        await GrantAsync(world.Tenant.PublicId);
        var (south, _, southCustomer, southTokens) = await AccountHttp.SignInTenantAdmin("South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "sam@south.example");
        var southToken = southTokens.AccessToken;
        var southAccount = await AccountHttp.OpenAccountAsync(southToken, southCustomer.PublicId, "South broker", "Brokerage", "NZD");
        var zzz = await CustodialHttp.CreateInstrumentAsync(southToken, "ZZZ", "NZD", price: null);
        await ClassifyAsync(southToken, zzz, "US", 2025, "Attributing");
        await PostAsync(southToken, Cash(southAccount, "TransferIn", 10_000.00m, "2024-05-01T00:00:00Z"));
        await PostAsync(southToken, Trade(southAccount, "Buy", zzz, 100m, 15.00m, "2024-06-03T01:00:00Z"));
        await AddPriceAsync(zzz, "NZD", new DateOnly(2025, 3, 31), 20.00m);
        await AddPriceAsync(zzz, "NZD", new DateOnly(2026, 3, 31), 22.00m);
        await GrantAsync(south.PublicId);
        await RunAsync(await RequestAsync(southToken, southCustomer.PublicId, 2026));

        var executionId = await RequestAsync(world.Token, world.Customer.PublicId, 2026);
        await RunAsync(executionId);

        var text = Text(await ReadFileAsync(world.Tenant.PublicId, executionId));
        text.ShouldContain("AAA");
        text.ShouldNotContain("ZZZ");
        text.ShouldNotContain("South broker");
        text.ShouldNotContain(southCustomer.Name);
    }

    private static async Task<Guid> RequestAsync(string token, Guid customerId, int taxYear)
    {
        var response = await TenantHttp.Send(HttpMethod.Post, "/reports/request", token, new
        {
            reportId = FifId,
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
        var response = await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenantId}/reports", system.AccessToken, new { reportId = FifId });
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
