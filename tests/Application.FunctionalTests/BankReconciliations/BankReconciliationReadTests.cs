using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.ClientMoneyAccounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.BankReconciliations.BankReconciliationHttp;

namespace MyWealthV2.Application.FunctionalTests.BankReconciliations;

/// <summary>Bank-reconciliation R19–R21: the overview, the list and its filters.</summary>
[Parallelizable]
public class BankReconciliationReadTests : TestBase
{
    [Test]
    public async Task List_IsNewestFirst_WithoutItemsFeedOrPaymentsInFlight_AndPages()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var first = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        var second = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        var third = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);

        var page = await ListAsync(world.TenantAdmin.AccessToken);

        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        Ids(page).ShouldBe([third, second, first]);
        var item = page.GetProperty("items")[0];
        item.TryGetProperty("items", out _).ShouldBeFalse();
        item.TryGetProperty("feed", out _).ShouldBeFalse();
        item.TryGetProperty("paymentsInFlight", out _).ShouldBeFalse();
        item.GetProperty("status").GetString().ShouldBe("NoBankBalance");
        item.GetProperty("clientMoneyAccount").GetProperty("id").GetGuid().ShouldBe(world.ClientMoney.PublicId);
        item.GetProperty("totals").GetProperty("breaks").GetProperty("count").GetInt32().ShouldBe(0);
        item.TryGetProperty("reservedCash", out _).ShouldBeTrue();
        Ids(await ListAsync(world.TenantAdmin.AccessToken, "?page=2&pageSize=2")).ShouldBe([first]);
    }

    [Test]
    public async Task List_FiltersByClientMoneyAccountStatusAndDay()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var nzd = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id, "NZD");
        var noBalance = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        await world.ReceiveAsync(10m, "Unknown ref");
        var breaks = await RunAsync(world.TenantAdmin.AccessToken, world.ClientMoney.PublicId);
        var nzdRun = await RunAsync(world.TenantAdmin.AccessToken, nzd.PublicId);
        var token = world.TenantAdmin.AccessToken;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");

        Ids(await ListAsync(token, $"?clientMoneyAccountId={nzd.PublicId}")).ShouldBe([nzdRun]);
        Ids(await ListAsync(token, "?status=Breaks")).ShouldBe([breaks]);
        // The pull of the receipt gave the NZD account a balance too, so its run agrees.
        Ids(await ListAsync(token, "?status=breaks,NoBankBalance")).ShouldBe([breaks, noBalance]);
        Ids(await ListAsync(token, "?status=Agreed")).ShouldBe([nzdRun]);
        Ids(await ListAsync(token, $"?clientMoneyAccountId={world.ClientMoney.PublicId}&status=NoBankBalance")).ShouldBe([noBalance]);
        Ids(await ListAsync(token, $"?from={today}&to={today}")).Count.ShouldBe(3);
        Ids(await ListAsync(token, $"?from={tomorrow}")).ShouldBeEmpty();
        Ids(await ListAsync(token, $"?to={yesterday}")).ShouldBeEmpty();
    }

    [TestCase("?status=Lost", "Status")]
    [TestCase("?status=1", "Status")]
    [TestCase("?from=2026-11-22&to=2026-11-21", "To")]
    [TestCase("?pageSize=101", "PageSize")]
    public async Task List_WithAFilterOutsideItsRule_Returns400(string query, string field)
    {
        var world = await DepositMatchingWorld.CreateAsync();

        var response = await Send(HttpMethod.Get, BankReconciliationHttp.Path + query, world.TenantAdmin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(field);
    }

    [Test]
    public async Task Overview_ListsEachActiveAccountAndEachInactiveOneWithARun_WithItsLatestRunAndFeed()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var system = await TenantHttp.SignInSystemAdmin();
        var nzd = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id, "NZD");
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, world.Tenant.PublicId, nzd.PublicId, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await world.ReceiveAsync(100m, world.DepositReference);
        await RunAsync(token, world.ClientMoney.PublicId);
        var latest = await RunAsync(token, world.ClientMoney.PublicId);
        // Opened after the last read: no feed status yet.
        var eur = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id, "EUR");
        // The old USD account, replaced, with a run; and the inactive NZD one without a run, which is not listed.
        (await ClientMoneyHttp.SetActiveAsync(system.AccessToken, world.Tenant.PublicId, world.ClientMoney.PublicId, active: false))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var replacementId = await ClientMoneyHttp.CreateAsync(system.AccessToken, world.Tenant.PublicId,
            ClientMoneyHttp.Body("USD", accountNumber: "SIM-NEW-" + Guid.NewGuid().ToString("N")[..8]));

        var overview = await OverviewAsync(token);

        var entries = overview.GetProperty("items").EnumerateArray().ToList();
        overview.GetProperty("totalCount").GetInt32().ShouldBe(3);
        entries.Select(entry => entry.GetProperty("clientMoneyAccount").GetProperty("id").GetGuid())
            .ShouldBe([eur.PublicId, replacementId, world.ClientMoney.PublicId]);
        entries[0].GetProperty("latest").ValueKind.ShouldBe(JsonValueKind.Null);
        entries[0].GetProperty("bankFeed").ValueKind.ShouldBe(JsonValueKind.Null);
        entries[1].GetProperty("latest").ValueKind.ShouldBe(JsonValueKind.Null);
        var old = entries[2];
        old.GetProperty("clientMoneyAccount").GetProperty("isActive").GetBoolean().ShouldBeFalse();
        old.GetProperty("clientMoneyAccount").GetProperty("accountNumber").GetString().ShouldBe(world.ClientMoney.AccountNumber);
        old.GetProperty("latest").GetProperty("id").GetGuid().ShouldBe(latest);
        old.GetProperty("latest").GetProperty("status").GetString().ShouldBe("Agreed");
        old.GetProperty("latest").GetProperty("bankBalance").GetDecimal().ShouldBe(100m);
        old.GetProperty("latest").TryGetProperty("items", out _).ShouldBeFalse();
        old.GetProperty("bankFeed").GetProperty("lastSucceededAt").ValueKind.ShouldBe(JsonValueKind.String);
        old.GetProperty("bankFeed").GetProperty("lastFailure").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Item_OfAScheduledRunHasNoPerson_AndANoBankBalanceRunHasNoBankFigures()
    {
        var world = await DepositMatchingWorld.CreateAsync();
        var id = await ScheduledRunAsync(world.ClientMoney.Id);

        var run = await GetAsync(world.TenantAdmin.AccessToken, id);

        run.GetProperty("trigger").GetString().ShouldBe("Scheduled");
        run.GetProperty("ranBy").ValueKind.ShouldBe(JsonValueKind.Null);
        Status(run).ShouldBe("NoBankBalance");
        run.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private static async Task<Guid> ScheduledRunAsync(int clientMoneyAccountId)
    {
        await using var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MyWealthV2.Application.BankReconciliations.IReconcileClientMoneyAccount>();
        return await service.RunAsync(clientMoneyAccountId, ReconciliationTrigger.Scheduled, ranByUserId: null, CancellationToken.None);
    }

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
