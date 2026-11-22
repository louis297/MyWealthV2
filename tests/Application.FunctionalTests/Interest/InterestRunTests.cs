using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.AccountClosures;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Hosting;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.Interest.Runs;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestDb;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R20, R30, R32, R37: <c>/interest/runs</c>, with a host that has the future as-of option on.</summary>
[Parallelizable]
public class InterestRunTests : TestBase
{
    private InterestRunHost _host = null!;

    [OneTimeSetUp]
    public void StartHost() => _host = new InterestRunHost();

    [OneTimeTearDown]
    public void StopHost() => _host.Dispose();

    [SetUp]
    public void ClearFailures() => _host.FailingAccounts.Clear();

    private async Task<HttpResponseMessage> StartOnHostAsync(string accessToken, Guid tenantId, DateOnly asOfDate)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Runs);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { tenantId, asOfDate = asOfDate.ToString("yyyy-MM-dd") });
        return await _host.Client.SendAsync(request);
    }

    private async Task<Guid> RunAsync(Guid tenantId, DateOnly asOfDate)
    {
        var system = await AccountHttp.SignInSystemAdmin();
        var response = await StartOnHostAsync(system.AccessToken, tenantId, asOfDate);
        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldEndWith($"{Runs}/{id}");
        return id;
    }

    private static async Task<(NominatedWorld World, Guid Account)> FundedAsync(string name = "North Advisory", string code = "north-advisory")
    {
        var world = await NominatedWorld.CreateAsync(name, code);
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", NextMonth);
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        (await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, account, 5000m)).IsSuccessStatusCode.ShouldBeTrue();
        return (world, account);
    }

    [Test]
    public async Task WithoutBearer_Returns401_OnEveryRoute()
    {
        (await Send(HttpMethod.Get, Runs, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, $"{Runs}/{Guid.NewGuid()}", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, Runs, null, new { tenantId = Guid.NewGuid(), asOfDate = "2026-12-31" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task OnlyASystemAdminStartsARun_AndOnlyTenantAdminAndSystemAdminRead()
    {
        var world = await NominatedWorld.CreateAsync();
        var body = new { tenantId = world.Tenant.PublicId, asOfDate = Today.AddDays(-1).ToString("yyyy-MM-dd") };

        foreach (var token in new[] { world.TenantAdmin.AccessToken, world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            (await Send(HttpMethod.Post, Runs, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        foreach (var token in new[] { world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            (await Send(HttpMethod.Get, Runs, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await Send(HttpMethod.Get, $"{Runs}/{Guid.NewGuid()}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await Send(HttpMethod.Get, Runs, world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Start_AnUnknownTenant_Returns404_AndAMissingTenantOrDate_Returns400()
    {
        var system = await AccountHttp.SignInSystemAdmin();

        (await Send(HttpMethod.Post, Runs, system.AccessToken, new { tenantId = Guid.NewGuid(), asOfDate = "2026-11-01" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Post, Runs, system.AccessToken, new { asOfDate = "2026-11-01" })))
            .Keys.ShouldContain("TenantId");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Post, Runs, system.AccessToken, new { tenantId = Guid.NewGuid() })))
            .Keys.ShouldContain("AsOfDate");
    }

    [Test]
    public async Task WithTheOptionOff_TodayReturns400_AndYesterdayReturns201()
    {
        var world = await NominatedWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        var today = await Send(HttpMethod.Post, Runs, system.AccessToken,
            new { tenantId = world.Tenant.PublicId, asOfDate = Today.ToString("yyyy-MM-dd") });
        (await CustodialHttp.ErrorsAsync(today))["AsOfDate"].ShouldBe([StartInterestRunCommandHandler.FutureMessage]);

        var yesterday = await Send(HttpMethod.Post, Runs, system.AccessToken,
            new { tenantId = world.Tenant.PublicId, asOfDate = Today.AddDays(-1).ToString("yyyy-MM-dd") });
        var id = await CustodialHttp.ReadIdAsync(yesterday);
        var item = await GetAsync(system.AccessToken, $"{Runs}/{id}");
        item.GetProperty("trigger").GetString().ShouldBe("Manual");
        item.GetProperty("ranBy").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        item.GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("completedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        item.GetProperty("postingCount").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task AMonthEndRun_PostsEachAccount_AndTheItemShowsTheCountsAndTotals()
    {
        var (world, account) = await FundedAsync();
        await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "NewZealand", true, 17.5m);
        var end = MonthEnd(NextMonth);

        var runId = await RunAsync(world.Tenant.PublicId, end);

        var posting = (await PostingsAsync(account)).ShouldHaveSingleItem();
        posting.SourcePublicId.ShouldBe(runId);
        var gross = posting.InterestDetail!.GrossAmount;
        var tax = -posting.CashLegs.Single(leg => leg.Role == CashLegRole.Tax).Amount;
        var item = await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}/{runId}");
        item.GetProperty("asOfDate").GetString().ShouldBe(end.ToString("yyyy-MM-dd"));
        item.GetProperty("postingDate").GetString().ShouldBe(end.ToString("yyyy-MM-dd"));
        item.GetProperty("accountsProcessed").GetInt32().ShouldBe(1);
        item.GetProperty("accountsFailed").GetInt32().ShouldBe(0);
        item.GetProperty("postingCount").GetInt32().ShouldBe(1);
        var total = item.GetProperty("totals").EnumerateArray().ShouldHaveSingleItem();
        total.GetProperty("currency").GetString().ShouldBe("USD");
        total.GetProperty("postingCount").GetInt32().ShouldBe(1);
        total.GetProperty("grossAmount").GetDecimal().ShouldBe(gross);
        total.GetProperty("taxAmount").GetDecimal().ShouldBe(tax);
    }

    [Test]
    public async Task TheSameRunAgain_IsStoredWithZeroPostings_AndNoSecondTransaction()
    {
        var (world, account) = await FundedAsync();
        var end = MonthEnd(NextMonth);
        await RunAsync(world.Tenant.PublicId, end);

        var again = await RunAsync(world.Tenant.PublicId, end);

        (await PostingsAsync(account)).Count.ShouldBe(1);
        var item = await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}/{again}");
        (item.GetProperty("accountsProcessed").GetInt32(), item.GetProperty("postingCount").GetInt32()).ShouldBe((1, 0));
        item.GetProperty("totals").GetArrayLength().ShouldBe(0);
        (await GetAsync(world.TenantAdmin.AccessToken, Runs)).GetProperty("totalCount").GetInt32().ShouldBe(2);
    }

    [Test]
    public async Task ADisabledCustomer_AndADisabledTenant_AreAccrued()
    {
        var (world, account) = await FundedAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        await RunAsync(world.Tenant.PublicId, NextMonth.AddDays(3));

        (await RowsAsync(account)).Count.ShouldBe(4);
    }

    [Test]
    public async Task ClosedClosingAndExternalAccounts_AreNotAccrued()
    {
        var (world, open) = await FundedAsync();
        var adviser = world.AdviserToken.AccessToken;
        var closed = await OpenCustodialAsync(world.Tenant.Id, adviser, world.Customer.PublicId);
        (await CustodialHttp.CloseAsync(adviser, closed)).IsSuccessStatusCode.ShouldBeTrue();
        var closing = await OpenCustodialAsync(world.Tenant.Id, adviser, world.Customer.PublicId);
        await CustodialHttp.FundAsync(adviser, closing, 300m);
        await AccountClosureHttp.RequestAsync(adviser, closing, await CustodialHttp.VerifiedPayeeAsync(closing));
        var external = await CustodialHttp.OpenExternalAsync(adviser, world.Customer.PublicId);
        await CustodialHttp.FundAsync(adviser, external, 5000m, external: true);

        var runId = await RunAsync(world.Tenant.PublicId, NextMonth.AddDays(3));

        (await AccruedThroughAsync(open)).ShouldBe(NextMonth.AddDays(3));
        foreach (var skipped in new[] { closed, closing, external })
        {
            (await AccruedThroughAsync(skipped)).ShouldBeNull();
            (await RowsAsync(skipped)).ShouldBeEmpty();
        }

        (await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}/{runId}")).GetProperty("accountsProcessed").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task OneAccountThatFails_DoesNotStopTheOthers_AndTheNextRunCompletesIt()
    {
        var (world, first) = await FundedAsync();
        var second = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        await CustodialHttp.FundAsync(world.AdviserToken.AccessToken, second, 2000m);
        _host.FailingAccounts.Add(await InternalIdAsync(first));
        var end = MonthEnd(NextMonth);

        var failedRun = await RunAsync(world.Tenant.PublicId, end);

        var item = await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}/{failedRun}");
        (item.GetProperty("accountsProcessed").GetInt32(), item.GetProperty("accountsFailed").GetInt32(), item.GetProperty("postingCount").GetInt32())
            .ShouldBe((2, 1, 1));
        (await PostingsAsync(first)).ShouldBeEmpty();
        (await AccruedThroughAsync(first)).ShouldBeNull();
        (await PostingsAsync(second)).Count.ShouldBe(1);

        _host.FailingAccounts.Clear();
        await RunAsync(world.Tenant.PublicId, end);

        (await PostingsAsync(first)).Count.ShouldBe(1);
        (await PostingsAsync(second)).Count.ShouldBe(1);
    }

    [Test]
    public void AHostOutsideDevelopment_WithTheOptionOn_FailsAtStartUp()
    {
        using var factory = new WebApiFactory(FunctionalTestSetup.ConnectionString, environment: "Production",
            settings: new Dictionary<string, string> { [InterestOptions.AllowFutureAsOfDatesKey] = "true" });

        StartupFailure.Capture(factory).ShouldContain(InterestOptions.AllowFutureAsOfDatesKey);
    }

    [Test]
    public async Task List_IsNewestFirst_FiltersByTriggerAndDate_AndASystemAdminNamesTheTenant()
    {
        var world = await NominatedWorld.CreateAsync();
        var older = await RunAsync(world.Tenant.PublicId, NextMonth);
        var newer = await RunAsync(world.Tenant.PublicId, NextMonth.AddDays(5));
        var system = await AccountHttp.SignInSystemAdmin();
        var admin = world.TenantAdmin.AccessToken;

        Ids(await GetAsync(admin, Runs)).ShouldBe([newer, older]);
        Ids(await GetAsync(admin, $"{Runs}?trigger=Manual,AccountClosure")).ShouldBe([newer, older]);
        Ids(await GetAsync(admin, $"{Runs}?trigger=Scheduled")).ShouldBeEmpty();
        Ids(await GetAsync(admin, $"{Runs}?from={NextMonth.AddDays(1):yyyy-MM-dd}")).ShouldBe([newer]);
        Ids(await GetAsync(admin, $"{Runs}?to={NextMonth:yyyy-MM-dd}")).ShouldBe([older]);
        Ids(await GetAsync(system.AccessToken, $"{Runs}?tenantId={world.Tenant.PublicId}")).ShouldBe([newer, older]);

        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?trigger=Daily", admin))).Keys.ShouldContain("Trigger");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get,
            $"{Runs}?from={NextMonth.AddDays(1):yyyy-MM-dd}&to={NextMonth:yyyy-MM-dd}", admin))).Keys.ShouldContain("To");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, Runs, system.AccessToken))).Keys.ShouldContain("TenantId");
        (await Send(HttpMethod.Get, $"{Runs}?tenantId={Guid.NewGuid()}", system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{Runs}?tenantId={world.Tenant.PublicId}", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsRunIs404_AndARunNeverAccruesTheOtherTenantsAccounts()
    {
        var (world, ours) = await FundedAsync();
        var (south, theirs) = await FundedAsync("South Advisory", "south-advisory");

        var ourRun = await RunAsync(world.Tenant.PublicId, NextMonth.AddDays(3));
        var theirRun = await RunAsync(south.Tenant.PublicId, NextMonth.AddDays(1));

        (await AccruedThroughAsync(ours)).ShouldBe(NextMonth.AddDays(3));
        (await AccruedThroughAsync(theirs)).ShouldBe(NextMonth.AddDays(1));
        (await Send(HttpMethod.Get, $"{Runs}/{theirRun}", world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Ids(await GetAsync(world.TenantAdmin.AccessToken, Runs)).ShouldBe([ourRun]);
        (await GetAsync(world.TenantAdmin.AccessToken, $"{Runs}/{ourRun}")).GetProperty("accountsProcessed").GetInt32().ShouldBe(1);
    }

    private static List<Guid> Ids(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private static async Task<int> InternalIdAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Accounts.IgnoreQueryFilters().Where(row => row.PublicId == accountId).Select(row => row.Id).SingleAsync();
    }
}
