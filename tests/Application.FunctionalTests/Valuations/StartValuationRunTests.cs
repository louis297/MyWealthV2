using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Interest;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.Valuations.Runs;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationRunHttp;

namespace MyWealthV2.Application.FunctionalTests.Valuations;

/// <summary>Valuation-run R1, R15, R24: <c>POST /valuation-runs</c>, a SystemAdmin's run through a date.</summary>
[Parallelizable]
public class StartValuationRunTests : TestBase
{
    private static DateOnly LastFinalDay => ValuationRunDb.LastFinalDay;

    [Test]
    public async Task Start_RunsAtOnce_AndAnswers201_WithTheRunAndItsCounts()
    {
        var world = await NominatedWorld.CreateAsync();
        var account = await InterestHttp.OpenCustodialAsync(world.Tenant.Id, world.TenantAdmin.AccessToken, world.Customer.PublicId);
        await ValuationRunDb.SetCreatedAsync(account, LastFinalDay.AddDays(-1));

        var id = await StartRunAsync(world.Tenant.PublicId, LastFinalDay);

        var system = await AccountHttp.SignInSystemAdmin();
        var item = await GetAsync(system.AccessToken, $"{Runs}/{id}");
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("throughDate").GetString().ShouldBe(LastFinalDay.ToString("yyyy-MM-dd"));
        item.GetProperty("trigger").GetString().ShouldBe("Manual");
        item.GetProperty("ranBy").GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
        item.GetProperty("completedAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
        (item.GetProperty("accountsProcessed").GetInt32(), item.GetProperty("accountsFailed").GetInt32(),
                item.GetProperty("daysValued").GetInt32(), item.GetProperty("daysIncomplete").GetInt32())
            .ShouldBe((1, 0, 2, 0));
        (await ValuationRunDb.ValuedThroughAsync(account)).ShouldBe(LastFinalDay);

        // No Idempotency-Key: a repeat stores a run with zero counts.
        var repeat = await StartRunAsync(world.Tenant.PublicId, LastFinalDay);
        repeat.ShouldNotBe(id);
        (await GetAsync(system.AccessToken, $"{Runs}/{repeat}")).GetProperty("daysValued").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Start_ThroughADayThatIsNotFinal_OrNotADate_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var day in new[] { LastFinalDay.AddDays(1), ValuationHttp.Today, ValuationHttp.Today.AddDays(5) })
        {
            var response = await StartAsync(system.AccessToken, world.Tenant.PublicId, day);
            (await CustodialHttp.ErrorsAsync(response))["ThroughDate"].ShouldBe([StartValuationRunCommandHandler.NotFinalMessage]);
        }

        foreach (var text in new[] { "11/03/2026", "2026-02-30", "" })
        {
            var response = await Send(HttpMethod.Post, Runs, system.AccessToken, new { tenantId = world.Tenant.PublicId, throughDate = text });
            (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain("ThroughDate");
        }

        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Post, Runs, system.AccessToken, new { tenantId = world.Tenant.PublicId })))
            .Keys.ShouldContain("ThroughDate");
        (await CustodialHttp.ErrorsAsync(await Send(HttpMethod.Post, Runs, system.AccessToken,
                new { throughDate = LastFinalDay.ToString("yyyy-MM-dd") })))
            .Keys.ShouldContain("TenantId");
    }

    [Test]
    public async Task Start_AnUnknownTenant_Returns404_AndADisabledTenantIsRun()
    {
        var world = await NominatedWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        (await StartAsync(system.AccessToken, Guid.NewGuid(), LastFinalDay)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await DisableTenantAsync(world.Tenant.Id);
        (await StartAsync(system.AccessToken, world.Tenant.PublicId, LastFinalDay)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task OnlyASystemAdminStartsARun_AndOnlyTenantAdminAndSystemAdminRead()
    {
        var world = await NominatedWorld.CreateAsync();

        foreach (var token in new[] { world.TenantAdmin.AccessToken, world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            (await StartAsync(token, world.Tenant.PublicId, LastFinalDay)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        foreach (var token in new[] { world.AdviserToken.AccessToken, world.CustomerToken.AccessToken })
        {
            (await Send(HttpMethod.Get, Runs, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await Send(HttpMethod.Get, $"{Runs}/{Guid.NewGuid()}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await Send(HttpMethod.Get, Runs, world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task WithoutBearer_Returns401_OnEveryRoute()
    {
        (await Send(HttpMethod.Get, Runs, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, $"{Runs}/{Guid.NewGuid()}", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Post, Runs, null, new { tenantId = Guid.NewGuid(), throughDate = "2026-03-11" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
