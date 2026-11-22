using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Reports;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using UglyToad.PdfPig;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestDb;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R27, R29, R34–R36, R38: the transaction and account items, the accrual rows and the Customer
/// Transactions Report after an interest posting.</summary>
[Parallelizable]
public class InterestReadTests : TestBase
{
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    private static async Task<(NominatedWorld World, Guid Account, Guid Posting, Guid Run)> PostedAsync()
    {
        var world = await NominatedWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m)).IsSuccessStatusCode.ShouldBeTrue();
        await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "NewZealand", true, 17.5m);
        var end = MonthEnd(NextMonth);
        var run = Guid.NewGuid();
        var (_, posting) = await AccrueAndPostAsync(account, end, end, run);
        return (world, account, posting!.Value, run);
    }

    [Test]
    public async Task TheTransactionItem_HasTheInterestDetail_TheTaxLeg_AndTheRunSource_ForEveryCaller()
    {
        var (world, account, posting, run) = await PostedAsync();
        var days = MonthEnd(NextMonth).DayNumber - NextMonth.DayNumber + 1;
        var gross = Round2(0.41095890m * days);
        var tax = Round2(gross * 0.175m);

        foreach (var token in new[] { world.TenantAdmin.AccessToken, world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            var item = await GetAsync(token, $"/transactions/{posting}");
            item.GetProperty("type").GetString().ShouldBe("Interest");
            item.GetProperty("memo").GetString().ShouldBe("Interest");
            item.GetProperty("source").GetProperty("kind").GetString().ShouldBe("InterestRun");
            item.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(run);
            var interest = item.GetProperty("interest");
            interest.GetProperty("periodFrom").GetString().ShouldBe(NextMonth.ToString("yyyy-MM-dd"));
            interest.GetProperty("periodTo").GetString().ShouldBe(MonthEnd(NextMonth).ToString("yyyy-MM-dd"));
            interest.GetProperty("days").GetInt32().ShouldBe(days);
            interest.GetProperty("accruedAmount").GetDecimal().ShouldBe(0.41095890m * days);
            interest.GetProperty("grossAmount").GetDecimal().ShouldBe(gross);
            var legs = item.GetProperty("cash").EnumerateArray().ToList();
            var principal = legs.Single(leg => leg.GetProperty("role").GetString() == "Principal");
            principal.GetProperty("amount").GetDecimal().ShouldBe(gross);
            principal.GetProperty("tax").ValueKind.ShouldBe(JsonValueKind.Null);
            var taxLeg = legs.Single(leg => leg.GetProperty("role").GetString() == "Tax");
            taxLeg.GetProperty("amount").GetDecimal().ShouldBe(-tax);
            taxLeg.GetProperty("accountId").GetGuid().ShouldBe(account);
            var detail = taxLeg.GetProperty("tax");
            detail.GetProperty("kind").GetString().ShouldBe("Rwt");
            detail.GetProperty("ratePercent").GetDecimal().ShouldBe(17.5m);
            detail.GetProperty("basisAmount").GetDecimal().ShouldBe(gross);
            detail.GetProperty("amount").GetDecimal().ShouldBe(tax);
            detail.GetProperty("currency").GetString().ShouldBe("USD");
            taxLeg.GetProperty("fee").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task TheTransactionList_ShowsTheInterestAndTax_AndOtherTransactionsHaveNone()
    {
        var (world, account, posting, _) = await PostedAsync();

        var list = await GetAsync(world.TenantAdmin.AccessToken, $"/transactions?accountId={account}");

        var items = list.GetProperty("items").EnumerateArray().ToList();
        var interest = items.Single(item => item.GetProperty("id").GetGuid() == posting);
        interest.GetProperty("interest").GetProperty("grossAmount").GetDecimal().ShouldBeGreaterThan(0m);
        interest.GetProperty("cash").EnumerateArray().Count(leg => leg.GetProperty("role").GetString() == "Tax").ShouldBe(1);
        var deposit = items.Single(item => item.GetProperty("id").GetGuid() != posting);
        deposit.GetProperty("interest").ValueKind.ShouldBe(JsonValueKind.Null);
        deposit.GetProperty("cash")[0].GetProperty("tax").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TheAccountItem_ShowsTheInterestAccruedSoFar_AndZeroOnceItIsPosted()
    {
        var world = await NominatedWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m);
        var external = await CustodialHttp.OpenExternalAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);

        var before = await GetAsync(world.CustomerToken.AccessToken, $"/accounts/{account}");
        before.GetProperty("accruedInterest").GetDecimal().ShouldBe(0m);
        before.GetProperty("interestAccruedThrough").ValueKind.ShouldBe(JsonValueKind.Null);

        await AccrueAndPostAsync(account, NextMonth.AddDays(10), NextMonth.AddDays(-1));
        var mid = await GetAsync(world.CustomerToken.AccessToken, $"/accounts/{account}");
        mid.GetProperty("accruedInterest").GetDecimal().ShouldBe(0.41095890m * 11);
        mid.GetProperty("interestAccruedThrough").GetString().ShouldBe(NextMonth.AddDays(10).ToString("yyyy-MM-dd"));
        var listed = (await GetAsync(world.AdviserToken.AccessToken, "/accounts")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == account);
        listed.GetProperty("accruedInterest").GetDecimal().ShouldBe(0.41095890m * 11);

        var end = MonthEnd(NextMonth);
        await AccrueAndPostAsync(account, end, end);
        (await GetAsync(world.TenantAdmin.AccessToken, $"/accounts/{account}")).GetProperty("accruedInterest").GetDecimal().ShouldBe(0m);

        var externalItem = await GetAsync(world.TenantAdmin.AccessToken, $"/accounts/{external}");
        externalItem.GetProperty("accruedInterest").GetDecimal().ShouldBe(0m);
        externalItem.GetProperty("interestAccruedThrough").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TheAccrualRows_AreNewestFirst_WithEveryFilter()
    {
        var (world, account, posting, _) = await PostedAsync();
        await AccrueAndPostAsync(account, MonthEnd(NextMonth).AddDays(3), MonthEnd(NextMonth));
        var path = $"/accounts/{account}/interest-accruals";
        var token = world.AdviserToken.AccessToken;
        var days = MonthEnd(NextMonth).DayNumber - NextMonth.DayNumber + 1;

        var all = await GetAsync(token, $"{path}?pageSize=100");
        all.GetProperty("totalCount").GetInt32().ShouldBe(days + 3);
        var first = all.GetProperty("items")[0];
        first.GetProperty("date").GetString().ShouldBe(MonthEnd(NextMonth).AddDays(3).ToString("yyyy-MM-dd"));
        first.GetProperty("balance").GetDecimal().ShouldBe(5000m + Round2(0.41095890m * days) - Round2(Round2(0.41095890m * days) * 0.175m));
        first.GetProperty("transactionId").ValueKind.ShouldBe(JsonValueKind.Null);
        first.GetProperty("scheduleId").GetGuid().ShouldNotBe(Guid.Empty);
        var last = all.GetProperty("items")[days + 2];
        last.GetProperty("date").GetString().ShouldBe(NextMonth.ToString("yyyy-MM-dd"));
        last.GetProperty("balance").GetDecimal().ShouldBe(5000m);
        last.GetProperty("ratePercent").GetDecimal().ShouldBe(3m);
        last.GetProperty("amount").GetDecimal().ShouldBe(0.41095890m);
        last.GetProperty("transactionId").GetGuid().ShouldBe(posting);

        (await GetAsync(token, $"{path}?posted=false")).GetProperty("totalCount").GetInt32().ShouldBe(3);
        (await GetAsync(token, $"{path}?posted=true")).GetProperty("totalCount").GetInt32().ShouldBe(days);
        (await GetAsync(token, $"{path}?transactionId={posting}")).GetProperty("totalCount").GetInt32().ShouldBe(days);
        (await GetAsync(token, $"{path}?transactionId={Guid.NewGuid()}")).GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await GetAsync(token, $"{path}?from={NextMonth.AddDays(2):yyyy-MM-dd}&to={NextMonth.AddDays(4):yyyy-MM-dd}"))
            .GetProperty("totalCount").GetInt32().ShouldBe(3);
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get,
            $"{path}?from={NextMonth.AddDays(4):yyyy-MM-dd}&to={NextMonth.AddDays(2):yyyy-MM-dd}", token))).Keys.ShouldContain("To");
    }

    [Test]
    public async Task TheAccrualRows_TheCustomersOwn_AnotherCustomersAccountIs404_AndAnExternalAccountIsEmpty()
    {
        var (world, account, _, _) = await PostedAsync();
        var external = await CustodialHttp.OpenExternalAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var others = await CustodialHttp.OpenExternalAsync(world.TenantAdmin.AccessToken, world.OtherCustomer.PublicId);

        (await GetAsync(world.CustomerToken.AccessToken, $"/accounts/{account}/interest-accruals")).GetProperty("totalCount").GetInt32()
            .ShouldBeGreaterThan(0);
        (await GetAsync(world.CustomerToken.AccessToken, $"/accounts/{external}/interest-accruals")).GetProperty("totalCount").GetInt32()
            .ShouldBe(0);
        (await Send(HttpMethod.Get, $"/accounts/{others}/interest-accruals", world.CustomerToken.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/accounts/{account}/interest-accruals", world.OtherCustomerToken.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/accounts/{account}/interest-accruals", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsAccrualRowsAndPosting_Are404()
    {
        var (_, account, posting, _) = await PostedAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");

        (await Send(HttpMethod.Get, $"/accounts/{account}/interest-accruals", south.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"/transactions/{posting}", south.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AHandReverseOfThePosting_AndAHandInterestOnTheCustodialAccount_Return400()
    {
        var (world, account, posting, _) = await PostedAsync();

        (await CustodialHttp.ReverseAsync(world.TenantAdmin.AccessToken, posting)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var hand = await CustodialHttp.PostAsync(world.TenantAdmin.AccessToken, CustodialHttp.Cash(account, "Interest", 5m));
        (await CustodialHttp.ErrorsAsync(hand)).Keys.ShouldContain("Type");
        (await PostingsAsync(account)).Count.ShouldBe(1);
    }

    [Test]
    public async Task TheCustomerTransactionsReport_ShowsTheNetCash_AndTheTaxColumn()
    {
        var (world, account, posting, _) = await PostedAsync();
        await GrantAsync(world.Tenant.PublicId);
        var item = await GetAsync(world.TenantAdmin.AccessToken, $"/transactions/{posting}");
        var legs = item.GetProperty("cash").EnumerateArray().ToList();
        var gross = legs.Single(leg => leg.GetProperty("role").GetString() == "Principal").GetProperty("amount").GetDecimal();
        var tax = legs.Single(leg => leg.GetProperty("role").GetString() == "Tax").GetProperty("amount").GetDecimal();

        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/request", world.TenantAdmin.AccessToken, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId = world.Customer.PublicId, from = "2026-01-01", to = "2099-01-01" }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        }

        var file = System.IO.Path.Combine(ReportStoragePaths.Root, world.Tenant.PublicId.ToString("D"), $"{executionId:D}.pdf");
        using var document = PdfDocument.Open(await File.ReadAllBytesAsync(file));
        var text = string.Join('\n', document.GetPages().Select(page => page.Text));
        text.ShouldContain("Tax");
        text.ShouldContain($"{gross + tax:0.00} USD");
        text.ShouldContain($"{tax:0.00} USD");
        account.ShouldNotBe(Guid.Empty);
    }

    private static async Task GrantAsync(Guid tenantId)
    {
        var system = await TenantHttp.SignInSystemAdmin();
        var response = await TenantHttp.Send(HttpMethod.Post, $"/tenants/{tenantId}/reports", system.AccessToken,
            new { reportId = CustomerTransactionsId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
