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
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Reports;

public class GenerateCustomerTransactionsTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task ManualRequest_ThenExecute_WritesAPdfForTheUtcWindow()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Pdf", "firm-pdf");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-pdf", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-pdf", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee Report", "cee@firm-pdf", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-pdf", "Password1!", "firm-pdf");

        var everyday = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Everyday", "Bank");
        var broker = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Broker book", "Brokerage");
        var closed = await OpenAccountAsync(admin.AccessToken, customer.PublicId, "Old tin", "Bank");
        var instrument = await CreateInstrumentAsync(admin.AccessToken, "VTI");

        (await PostCashAsync(admin.AccessToken, everyday, 10.50m, "2026-01-02T00:00:00Z", "salary"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var salary = await LatestTransactionIdAsync(everyday);
        (await ReverseAsync(admin.AccessToken, salary)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, everyday, 1.00m, "2026-06-01T00:00:00Z", "june-transfer"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostOpeningAsync(admin.AccessToken, broker, instrument, "2026-03-01T00:00:00Z"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, closed, 3.00m, "2026-04-01T00:00:00Z", "closed-keep"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostCashAsync(admin.AccessToken, closed, -3.00m, "2026-04-01T01:00:00Z", "closed-out"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await CloseAsync(admin.AccessToken, closed);
        (await PostCashAsync(admin.AccessToken, everyday, 4.00m, "2099-01-01T00:00:00Z", "outside-window"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            admin.AccessToken,
            new
            {
                reportId = CustomerTransactionsId,
                parameters = new { customerId = customer.PublicId, from = "2026-01-01", to = "2099-01-01" }
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();

        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Succeeded");
        row.ResultStorageKey.ShouldBe($"{tenant.PublicId:D}/{executionId:D}.pdf");
        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        var pdf = await File.ReadAllBytesAsync(path);
        Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");

        var text = ReadPdf(pdf);
        text.ShouldContain("Customer Transactions Report");
        text.ShouldContain("Tenant: Firm Pdf");
        text.ShouldContain("Customer: Cee Report");
        text.ShouldContain("From: 2026-01-01");
        text.ShouldContain("To: 2099-01-01");
        text.ShouldContain("Include closed accounts: no");
        text.ShouldContain("10.50 NZD");
        text.ShouldContain("-10.50 NZD");
        text.ShouldContain("VTI");
        text.ShouldContain("12.5");
        text.ShouldNotContain("closed-keep");
        text.ShouldNotContain("outside-window");
        var salaryAt = text.IndexOf("salary", StringComparison.Ordinal);
        var reversalAt = text.IndexOf("Reversal", StringComparison.Ordinal);
        var openingAt = text.IndexOf("VTI", StringComparison.Ordinal);
        var juneAt = text.IndexOf("june-transfer", StringComparison.Ordinal);
        salaryAt.ShouldBeGreaterThanOrEqualTo(0);
        reversalAt.ShouldBeGreaterThan(salaryAt);
        openingAt.ShouldBeGreaterThan(reversalAt);
        juneAt.ShouldBeGreaterThan(openingAt);
    }

    [Test]
    public async Task Execute_WhenParametersDoNotBind_FailsWithoutAFile()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Fail", "firm-fail");
        await TestApp.CreatePersonAsync(
            UserRole.TenantAdmin, "Tess", "tess@firm-fail", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Ann", "ann@firm-fail", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cee Fail", "cee@firm-fail", "Password1!", tenant.Id, adviser.Id);
        await GrantAsync(tenant.PublicId);
        var admin = await FunctionalTestSetup.Oidc.SignInAsync("tess@firm-fail", "Password1!", "firm-fail");
        var created = await TenantHttp.Send(
            HttpMethod.Post,
            "/reports/request",
            admin.AccessToken,
            new
            {
                reportId = CustomerTransactionsId,
                parameters = new { customerId = customer.PublicId, from = "2026-01-01", to = "2026-02-01" }
            });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await ReplaceParametersAsync(executionId, """{"unknown":true}""");

        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Failed");
        row.ResultStorageKey.ShouldBeNull();
        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        File.Exists(path).ShouldBeFalse();
    }

    [Test]
    public async Task Execute_WhenTheReportHasNoGenerator_FailsWithoutAFile()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm Book", "firm-book");
        var executionId = await QueueAdviserBalancesAsync(tenant.Id);

        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var row = await ReadExecutionAsync(executionId);
        row.Status.ShouldBe("Failed");
        row.ResultStorageKey.ShouldBeNull();
        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        File.Exists(path).ShouldBeFalse();
    }

    private static async Task<Guid> QueueAdviserBalancesAsync(int tenantId)
    {
        var executionId = Guid.NewGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportId = await db.Reports
            .Where(row => row.Code == "adviser-balances")
            .Select(row => row.Id)
            .SingleAsync();
        var parameters = "{}";
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [ReportExecutions]
                ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson],
                 [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES
                ({executionId}, {reportId}, {tenantId}, N'Manual', N'Queued', {parameters},
                 {now}, N'test', {now}, N'test')
            """);
        return executionId;
    }

    private static async Task ReplaceParametersAsync(Guid executionId, string parametersJson)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [ReportExecutions]
            SET [ParametersJson] = {parametersJson}
            WHERE [PublicId] = {executionId}
            """);
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
            new { reportId = CustomerTransactionsId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string name, string type)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type,
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostCashAsync(
        string accessToken,
        Guid accountId,
        decimal amount,
        string bookedAt,
        string memo) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = amount < 0 ? "TransferOut" : "TransferIn",
            amount,
            bookedAt,
            memo
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> PostOpeningAsync(
        string accessToken,
        Guid accountId,
        Guid instrumentId,
        string bookedAt) =>
        TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "OpeningHoldings",
            bookedAt,
            security = new[] { new { instrumentId, quantity = 12.5m, cost = 100m } }
        }, Guid.NewGuid());

    private static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id) =>
        TransactionHttp.Send(HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, body: null, Guid.NewGuid());

    private static async Task<Guid> LatestTransactionIdAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountId);
        var transaction = await db.Transactions
            .Where(row => row.AccountId == account.Id)
            .OrderByDescending(row => row.Id)
            .FirstAsync();
        return transaction.PublicId;
    }

    private static async Task CloseAsync(string accessToken, Guid accountId)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var close = await AccountHttp.Send(
            HttpMethod.Post,
            $"/accounts/{accountId}/close",
            accessToken,
            new { rowVersion = json.RootElement.GetProperty("rowVersion").GetString() });
        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<(string Status, string? ResultStorageKey)> ReadExecutionAsync(Guid executionId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.ReportExecutions.AsNoTracking().SingleAsync(execution => execution.PublicId == executionId);
        return (row.Status, row.ResultStorageKey);
    }
}
