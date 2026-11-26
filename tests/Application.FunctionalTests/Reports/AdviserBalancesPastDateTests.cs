using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.NetWorth;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using UglyToad.PdfPig;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.Reports;

/// <summary>Valuation-history R17–R19 and §1.1: the Adviser Balances Report for the end of a past day, through the job.</summary>
[Parallelizable]
public class AdviserBalancesPastDateTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [Test]
    public async Task TheWorkedExample_ForAna_ReadsA1FromItsStoredValuation_AndAnExternalAccountFromTheLedger()
    {
        var world = await FundsWorld.CreateAsync();
        var external = await AccountHttp.OpenAccountAsync(world.AdminToken, world.A.Customer.PublicId, "Elsewhere", "Bank", "NZD");
        await PostAsync(world.AdminToken, Cash(external, "TransferIn", 500.00m, "2026-03-10T09:00:00Z"));
        await PostAsync(world.AdminToken, Cash(external, "TransferIn", 100.00m, "2026-03-12T09:00:00Z"));
        await world.AtTenAsync();
        // A price inserted after the day was stored changes nothing in the stored valuation (valuation-run R10).
        await AddPriceAsync(world.A.Ccc, "USD", FundsWorld.Day(11), 43.00m);

        var text = await GenerateAsync(world, world.A.Adviser.PublicId);

        text.ShouldContain("Balances at the end of 2026-03-11 (UTC)");
        text.ShouldContain("Incomplete: 1 holdings not valued, 0 accounts not valued yet. The subtotals leave them out.");
        text.ShouldContain("14356.60 NZD");
        text.ShouldContain("2.61 (2026-03-11)");
        text.ShouldContain("3915.00 NZD");
        text.ShouldContain("95.20 (2026-03-11)");
        text.ShouldContain("9520.00 USD");
        text.ShouldContain("41.30 (2026-03-06)");
        text.ShouldContain("Price too old");
        text.ShouldContain("500.00 NZD");
        text.ShouldNotContain("600.00 NZD");
        text.ShouldNotContain("Cost");
        text.ShouldContain("NZD cash 14356.60 NZD market value 3915.00 NZD");
        text.ShouldContain("USD cash 0.00 USD market value 9520.00 USD");
        text.ShouldContain("NZD cash 500.00 NZD market value 0.00 NZD");
        text.ShouldContain("NZD cash 14856.60 NZD market value 3915.00 NZD");
        text.ShouldNotContain("Bea Customer");
    }

    [Test]
    public async Task ACustodialAccountWithNoStoredValuationOfTheDay_IsNotValuedYet()
    {
        var world = await FundsWorld.CreateAsync();
        await world.AtTenAsync();

        var text = await GenerateAsync(world, world.Ben.PublicId);

        text.ShouldContain("B1");
        text.ShouldContain("Not valued yet");
        text.ShouldContain("Incomplete: 0 holdings not valued, 1 accounts not valued yet.");
        text.ShouldNotContain("7500.25");
    }

    private static async Task<string> GenerateAsync(FundsWorld world, Guid adviserId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{world.Tenant.PublicId}/reports", system.AccessToken, new { reportId = AdviserBalancesId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/request", world.AdminToken, new
        {
            reportId = AdviserBalancesId,
            parameters = new { adviserId, date = "2026-03-11" }
        });
        var content = await created.Content.ReadAsStringAsync();
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted, content);
        using var json = JsonDocument.Parse(content);
        var executionId = json.RootElement.GetProperty("id").GetGuid();

        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        }

        var path = Path.Combine(ReportStoragePaths.Root, world.Tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        File.Exists(path).ShouldBeTrue("The report job did not store a PDF.");
        using var document = PdfDocument.Open(await File.ReadAllBytesAsync(path));
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }
}
