using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationRunHttp;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Valuation-run R22, R23: the runs of a Tenant, and the cross-tenant 404.</summary>
[Parallelizable]
public class GetValuationRunsTests : TestBase
{
    private static readonly DateOnly Older = new(2026, 3, 4);
    private static readonly DateOnly Newer = new(2026, 3, 11);

    [Test]
    public async Task List_IsNewestFirst_FiltersByTriggerAndThroughDate_AndASystemAdminNamesTheTenant()
    {
        var world = await NominatedWorld.CreateAsync();
        var older = (await ValuationRunDb.RunAsync(world.Tenant.Id, Older)).PublicId;
        var newer = (await ValuationRunDb.RunAsync(world.Tenant.Id, Newer, ValuationRunTrigger.Manual, world.Adviser.Id)).PublicId;
        var system = await AccountHttp.SignInSystemAdmin();
        var admin = world.TenantAdmin.AccessToken;

        var list = await GetAsync(admin, Runs);
        Ids(list).ShouldBe([newer, older]);
        (list.GetProperty("page").GetInt32(), list.GetProperty("pageSize").GetInt32(), list.GetProperty("totalCount").GetInt32())
            .ShouldBe((1, 20, 2));
        Ids(await GetAsync(admin, $"{Runs}?trigger=Manual")).ShouldBe([newer]);
        Ids(await GetAsync(admin, $"{Runs}?trigger=Scheduled,Manual")).ShouldBe([newer, older]);
        Ids(await GetAsync(admin, $"{Runs}?from=2026-03-05")).ShouldBe([newer]);
        Ids(await GetAsync(admin, $"{Runs}?to=2026-03-04")).ShouldBe([older]);
        Ids(await GetAsync(admin, $"{Runs}?pageSize=1&page=2")).ShouldBe([older]);
        Ids(await GetAsync(system.AccessToken, $"{Runs}?tenantId={world.Tenant.PublicId}")).ShouldBe([newer, older]);

        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?trigger=Daily", admin))).Keys.ShouldContain("Trigger");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?trigger=0", admin))).Keys.ShouldContain("Trigger");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?from=4-3-2026", admin))).Keys.ShouldContain("From");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?to=soon", admin))).Keys.ShouldContain("To");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, $"{Runs}?from=2026-03-11&to=2026-03-04", admin)))
            .Keys.ShouldContain("From");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Get, Runs, system.AccessToken))).Keys.ShouldContain("TenantId");
        (await Send(HttpMethod.Get, $"{Runs}?tenantId={Guid.NewGuid()}", system.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{Runs}?tenantId={world.Tenant.PublicId}", admin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Item_HasTheFieldsOfR22_AndRanByIsThePersonOrNull()
    {
        var world = await NominatedWorld.CreateAsync();
        var scheduled = (await ValuationRunDb.RunAsync(world.Tenant.Id, Older)).PublicId;
        var manual = (await ValuationRunDb.RunAsync(world.Tenant.Id, Newer, ValuationRunTrigger.Manual, world.Adviser.Id)).PublicId;
        var admin = world.TenantAdmin.AccessToken;

        var item = await GetAsync(admin, $"{Runs}/{manual}");
        item.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "throughDate", "trigger", "ranBy", "startedAt", "completedAt", "accountsProcessed", "accountsFailed",
            "daysValued", "daysIncomplete"
        ]);
        item.GetProperty("ranBy").GetProperty("id").GetGuid().ShouldBe(world.Adviser.PublicId);
        item.GetProperty("ranBy").GetProperty("name").GetString().ShouldBe(world.Adviser.Name);
        item.GetProperty("throughDate").GetString().ShouldBe("2026-03-11");
        (await GetAsync(admin, $"{Runs}/{scheduled}")).GetProperty("ranBy").ValueKind.ShouldBe(JsonValueKind.Null);
        (await GetAsync(admin, $"{Runs}/{scheduled}")).GetProperty("trigger").GetString().ShouldBe("Scheduled");

        var listed = (await GetAsync(admin, Runs)).GetProperty("items").EnumerateArray().First();
        listed.EnumerateObject().Select(property => property.Name).ShouldBe(item.EnumerateObject().Select(property => property.Name));
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsRunIs404_AndTheListShowsOnlyOurs()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var ours = (await ValuationRunDb.RunAsync(world.Tenant.Id, Older)).PublicId;
        var theirs = (await ValuationRunDb.RunAsync(south.Tenant.Id, Older)).PublicId;
        var admin = world.TenantAdmin.AccessToken;

        (await Send(HttpMethod.Get, $"{Runs}/{theirs}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{Runs}/{Guid.NewGuid()}", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, $"{Runs}/12", admin)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Ids(await GetAsync(admin, Runs)).ShouldBe([ours]);
        Ids(await GetAsync(south.TenantAdmin.AccessToken, Runs)).ShouldBe([theirs]);
    }
}
