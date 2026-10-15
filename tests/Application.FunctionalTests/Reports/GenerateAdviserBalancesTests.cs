using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.MarketData;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GenerateAdviserBalancesTests : TestBase
{
    private static readonly Guid AdviserBalancesId = Guid.Parse("5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22");

    [Test]
    public async Task Execute_WritesCashAndHoldings_AndOmitsZerosClosedAndOtherBooks()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Book Pdf", "firm-book-pdf");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-book-pdf", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann Book", "ann@firm-book-pdf", "Password1!", tenant.Id);
        var otherAdviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bea", "bea@firm-book-pdf", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee Book", "cee@firm-book-pdf", "Password1!", tenant.Id, adviser.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee Hidden", "dee@firm-book-pdf", "Password1!", tenant.Id, otherAdviser.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-book-pdf", "Password1!", "firm-book-pdf");

        var broker = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Broker book", "Brokerage", "NZD");
        var dollar = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Dollar book", "Bank", "USD");
        var everyday = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Everyday book", "Bank", "NZD");
        var flat = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Flat book", "Bank", "NZD");
        var closed = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Old book", "Bank", "NZD");
        var gone = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Gone book", "Brokerage", "NZD");
        var hidden = await OpenAccountAsync(admin.AccessToken, otherCustomer.PublicId, "Hidden book", "Bank", "NZD");
        var priced = await CreateInstrumentAsync(admin.AccessToken, "VTI", "USD");
        var unpriced = await CreateInstrumentAsync(admin.AccessToken, "AIA", "NZD");
        var removed = await CreateInstrumentAsync(admin.AccessToken, "GONE", "NZD");
        await SetPriceAsync(priced, 20m, "USD");

        (await PostCashAsync(admin.AccessToken, everyday, 10.50m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, dollar, 3m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, flat, 4m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, flat, -4m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, closed, 8m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, hidden, 99m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(admin.AccessToken, broker, priced, 12.5m, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(admin.AccessToken, broker, unpriced, 2m, 50m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var opening = await PostOpeningAsync(admin.AccessToken, gone, removed, 1m, 9m);
        opening.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var openingJson = JsonDocument.Parse(await opening.Content.ReadAsStringAsync());
        (await ReverseAsync(admin.AccessToken, openingJson.RootElement.GetProperty("id").GetGuid()))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await MarkClosedAsync(closed);

        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            admin.AccessToken,
            new { reportId = AdviserBalancesId, parameters = new { adviserId = adviser.PublicId } });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        var queued = await ReadExecutionAsync(executionId);

        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded");
        row.ResultStorageKey.ShouldBe($"{tenant.PublicId:D}/{executionId:D}.pdf");
        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        var pdf = await File.ReadAllBytesAsync(path);
        Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");

        var text = ReadPdf(pdf);
        var asOf = queued.AsOf!.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
        text.ShouldContain("Adviser Balances Report");
        text.ShouldContain("Tenant: Firm Book Pdf");
        text.ShouldContain("Adviser: Ann Book");
        text.ShouldContain($"As of: {asOf} UTC");
        text.ShouldContain("Include closed accounts: no");
        text.ShouldContain("Cee Book");
        text.ShouldContain("10.50 NZD");
        text.ShouldContain("3.00 USD");
        text.ShouldContain("VTI");
        text.ShouldContain("12.5");
        text.ShouldContain("100.00 USD");
        text.ShouldContain("250.00 USD");
        text.ShouldContain("AIA");
        text.ShouldContain("0.00 NZD");
        text.ShouldContain("NZD cash 10.50 NZD market value 0.00 NZD");
        text.ShouldContain("USD cash 3.00 USD market value 250.00 USD");
        text.ShouldNotContain("Dee Hidden");
        text.ShouldNotContain("Hidden book");
        text.ShouldNotContain("Old book");
        text.ShouldNotContain("Flat book");
        text.ShouldNotContain("Gone book");
        text.ShouldNotContain("GONE");
        text.ShouldNotContain("8.00 NZD");
        text.ShouldNotContain("99.00 NZD");

        var aia = text.IndexOf("AIA", StringComparison.Ordinal);
        var vti = text.IndexOf("VTI", StringComparison.Ordinal);
        var dollarAt = text.IndexOf("3.00 USD", StringComparison.Ordinal);
        var everydayAt = text.IndexOf("10.50 NZD", StringComparison.Ordinal);
        var subtotals = text.IndexOf("Subtotals", StringComparison.Ordinal);
        aia.ShouldBeGreaterThanOrEqualTo(0);
        vti.ShouldBeGreaterThan(aia);
        dollarAt.ShouldBeGreaterThan(vti);
        everydayAt.ShouldBeGreaterThan(dollarAt);
        subtotals.ShouldBeGreaterThan(everydayAt);
    }

    private static string ReadPdf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join('\n', document.GetPages().Select(page => page.Text));
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

    private static async Task<Guid> OpenAccountAsync(
        string accessToken,
        Guid customerId,
        string name,
        string type,
        string currency)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol, string quoteCurrency)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, string currency)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        var prices = FunctionalTestSetup.WebApiServices.GetRequiredService<MarketDataMock>();
        prices.SetPrice(instrument.Id, Money.Create(amount, currency));
    }

    private static Task<HttpResponseMessage> PostCashAsync(string accessToken, Guid accountId, decimal amount) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = amount < 0 ? "TransferOut" : "TransferIn",
            amount,
            bookedAt = "2026-10-01T00:00:00Z"
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        Guid instrumentId,
        decimal quantity,
        decimal cost) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt = "2026-10-01T00:00:00Z",
            security = new[] { new { instrumentId, quantity, cost } }
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id) =>
        TransactionHttp.Send(HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, body: null, Guid.NewGuid());

    private static async Task MarkClosedAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Accounts] SET [Status] = {1} WHERE [PublicId] = {accountId}");
        updated.ShouldBe(1);
    }

    private static async Task<(string Status, string? ResultStorageKey, DateTimeOffset? AsOf)> ReadExecutionAsync(
        Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.AsNoTracking().SingleAsync(execution => execution.PublicId == executionId);
        return (row.Status, row.ResultStorageKey, row.AsOf);
    }
}
