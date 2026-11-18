using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Application.FunctionalTests.Reports;
using MyWealthV2.Application.Reports;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>Trade fees R22, §8.1: cash leg <c>role</c> and <c>fee</c> in the item, and the report Fee column.</summary>
[Parallelizable]
public class TradeFeeReaderTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task BuyItem_ShowsTheRoleOnEveryLegAndTheFeeObjectOnTheFeeLeg()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        var scheduleId = await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        var buyId = await TradeFeeTradeTests.ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        using var item = await GetAsync(admin.AccessToken, $"/transactions/{buyId}");

        var legs = TransactionJson.CashLegs(item.RootElement);
        legs.Count.ShouldBe(2);
        var principal = legs.Single(leg => leg.GetProperty("role").GetString() == "Principal");
        principal.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        principal.GetProperty("customerId").GetGuid().ShouldBe(customer.PublicId);
        principal.GetProperty("amount").GetDecimal().ShouldBe(-1250m);
        principal.GetProperty("currency").GetString().ShouldBe("USD");
        principal.GetProperty("fee").ValueKind.ShouldBe(JsonValueKind.Null);

        var fee = legs.Single(leg => leg.GetProperty("role").GetString() == "Fee");
        fee.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        fee.GetProperty("amount").GetDecimal().ShouldBe(-10m);
        fee.GetProperty("currency").GetString().ShouldBe("USD");
        var detail = fee.GetProperty("fee");
        detail.GetProperty("kind").GetString().ShouldBe("Brokerage");
        detail.GetProperty("scheduleId").GetGuid().ShouldBe(scheduleId);
        detail.GetProperty("basisAmount").GetDecimal().ShouldBe(1250m);
        detail.GetProperty("amount").GetDecimal().ShouldBe(10m);
        detail.GetProperty("currency").GetString().ShouldBe("USD");

        TransactionJson.SecurityLegs(item.RootElement).ShouldHaveSingleItem().GetProperty("cost").GetDecimal().ShouldBe(1260m);
    }

    [Test]
    public async Task CrossCurrencyBuy_FeeAmountIsInTheQuoteCurrency_LegAmountInTheAccountCurrency()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var accountId = await FeeHttp.OpenCustodialAsync(admin.AccessToken, customer.PublicId, "NZD");
        var instrumentId = await FeeHttp.CreateInstrumentAsync(admin.AccessToken, "VTI", "USD");
        await FeeHttp.SetPriceAsync(instrumentId, 25.00m, "USD");
        await TradeFeeTradeTests.SetRateAsync("USD", "NZD", 1.6543m);
        (await FeeHttp.FundCustodialAsync(admin.AccessToken, accountId, 10000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        var buyId = await TradeFeeTradeTests.ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        using var item = await GetAsync(admin.AccessToken, $"/transactions/{buyId}");

        var fee = TransactionJson.CashLegs(item.RootElement).Single(leg => leg.GetProperty("role").GetString() == "Fee");
        fee.GetProperty("amount").GetDecimal().ShouldBe(-16.543m);
        fee.GetProperty("currency").GetString().ShouldBe("NZD");
        fee.GetProperty("fee").GetProperty("amount").GetDecimal().ShouldBe(10m);
        fee.GetProperty("fee").GetProperty("currency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task OtherTypes_ShowPrincipalWithANullFee_InTheItemAndTheList()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var accountId = await FeeHttp.OpenAccountAsync(admin.AccessToken, customer.PublicId, "Bank", "NZD");
        var transferId = await TradeFeeTradeTests.ReadIdAsync(await FeeHttp.PostCashAsync(admin.AccessToken, accountId, 100m));

        using var item = await GetAsync(admin.AccessToken, $"/transactions/{transferId}");
        var leg = TransactionJson.SingleCash(item.RootElement);
        leg.GetProperty("role").GetString().ShouldBe("Principal");
        leg.TryGetProperty("fee", out var fee).ShouldBeTrue();
        fee.ValueKind.ShouldBe(JsonValueKind.Null);

        using var list = await GetAsync(admin.AccessToken, $"/transactions?accountId={accountId}");
        var row = list.RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        TransactionJson.SingleCash(row).GetProperty("role").GetString().ShouldBe("Principal");
    }

    [Test]
    public async Task List_ShowsBothLegsOfATradeWithAFee()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        await TradeFeeTradeTests.ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        using var list = await GetAsync(admin.AccessToken, $"/transactions?accountId={accountId}&type=Buy");

        var row = list.RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        TransactionJson.CashLegs(row).Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Principal", -1250m), ("Fee", -10m)], ignoreOrder: true);
    }

    [Test]
    public async Task CustomerTransactionsReport_ShowsTheNetCashAndTheFee()
    {
        var (tenant, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        await GrantAsync(tenant.PublicId);
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.Today);
        await TradeFeeTradeTests.ReadIdAsync(
            await FeeHttp.PostTradeAsync(admin.AccessToken, accountId, "Buy", instrumentId, 50m));

        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/request", admin.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = customer.PublicId, from = "2026-01-01", to = "2099-01-01" }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);

        var path = Path.Combine(ReportStoragePaths.Root, tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        File.Exists(path).ShouldBeTrue();
        using var document = PdfDocument.Open(await File.ReadAllBytesAsync(path));
        var text = string.Join('\n', document.GetPages().Select(page => page.Text));
        text.ShouldContain("Fee");
        text.ShouldContain("-1260.00 USD");
        text.ShouldContain("-10.00 USD");
        text.ShouldNotContain("-1250.00 USD");
    }

    private static async Task<JsonDocument> GetAsync(string accessToken, string path)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
}
