using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using static MyWealthV2.Application.FunctionalTests.Interest.InterestHttp;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R1, R9–R15: <c>/interest/rate-schedules</c>.</summary>
[Parallelizable]
public class InterestRateScheduleTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_WithoutRedirect()
    {
        var id = Guid.NewGuid();
        foreach (var (method, path, body) in Routes(id))
        {
            var response = await Send(method, path, null, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task CustomerAndSystemAdmin_Return403_OnEveryRoute()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.CustomerToken.AccessToken, system.AccessToken })
        {
            foreach (var (method, path, body) in Routes(id))
            {
                (await Send(method, path, token, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            }
        }
    }

    [Test]
    public async Task Adviser_Reads_ButEveryWriteReturns403()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));
        var token = world.AdviserToken.AccessToken;
        var rowVersion = await RowVersionAsync(world.TenantAdmin.AccessToken, id);

        (await GetAsync(token, Schedules)).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await GetAsync(token, $"{Schedules}/{id}")).GetProperty("id").GetGuid().ShouldBe(id);
        (await Send(HttpMethod.Post, Schedules, token, ScheduleBody("USD", Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Put, $"{Schedules}/{id}", token, UpdateBody(Today.AddDays(11), rowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Delete, $"{Schedules}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DisabledTenant_Returns403()
    {
        var world = await NominatedWorld.CreateAsync();
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        (await Send(HttpMethod.Get, Schedules, world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_Creates_AndGetReturnsTheItem_Pending()
    {
        var world = await NominatedWorld.CreateAsync();

        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, " usd ", Today.AddDays(9), Band(0m, 3m), Band(100000.00m, 2m));

        var item = await GetAsync(world.TenantAdmin.AccessToken, $"{Schedules}/{id}");
        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("effectiveFrom").GetString().ShouldBe(Today.AddDays(9).ToString("yyyy-MM-dd"));
        var bands = item.GetProperty("bands").EnumerateArray()
            .Select(band => (band.GetProperty("from").GetDecimal(), band.GetProperty("ratePercent").GetDecimal())).ToList();
        bands.ShouldBe([(0m, 3m), (100000m, 2m)]);
        item.GetProperty("inEffect").GetBoolean().ShouldBeFalse();
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task List_FiltersByCurrency_AndSortsByCurrencyThenDate()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var usdLate = await CreateScheduleAsync(token, "USD", Today.AddDays(40));
        var nzd = await CreateScheduleAsync(token, "NZD", Today.AddDays(5));
        var usdEarly = await CreateScheduleAsync(token, "USD", Today.AddDays(10));

        Ids(await GetAsync(token, Schedules)).ShouldBe([nzd, usdEarly, usdLate]);
        Ids(await GetAsync(token, $"{Schedules}?currency=usd")).ShouldBe([usdEarly, usdLate]);
    }

    [Test]
    public async Task ATenantIdFromATenantAdmin_Returns400_OnCreateAndList()
    {
        var world = await NominatedWorld.CreateAsync();
        var body = ScheduleBody("USD", Today.AddDays(10));
        body["tenantId"] = world.Tenant.PublicId;

        (await Send(HttpMethod.Post, Schedules, world.TenantAdmin.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Get, $"{Schedules}?tenantId={world.Tenant.PublicId}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountSchedulesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_EffectiveFromTodayOrEarlier_Returns400_TomorrowIsAccepted()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;

        foreach (var day in new[] { Today, Today.AddDays(-3) })
        {
            var response = await Send(HttpMethod.Post, Schedules, token, ScheduleBody("USD", day));
            (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain("EffectiveFrom");
        }

        await CreateScheduleAsync(token, "USD", Today.AddDays(1));
    }

    [Test]
    public async Task Create_ADuplicateDate_Returns400_AtEffectiveFrom()
    {
        var world = await NominatedWorld.CreateAsync();
        await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));

        var response = await Send(HttpMethod.Post, Schedules, world.TenantAdmin.AccessToken, ScheduleBody("USD", Today.AddDays(10)));

        (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain("EffectiveFrom");
        (await CountSchedulesAsync()).ShouldBe(1);
    }

    private static IEnumerable<TestCaseData> InvalidShapes()
    {
        var day = Today.AddDays(10);
        yield return new TestCaseData(Set(ScheduleBody("USD", day), "bands", Array.Empty<object>()), "Bands")
            .SetName("Create_NoBands_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Enumerable.Range(0, 11).Select(i => Band(i * 100m, 1m)).ToArray()), "Bands")
            .SetName("Create_ElevenBands_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(5m, 3m)), "Bands[0].From").SetName("Create_FirstBandNotAtZero_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(0m, 3m), Band(100m, 2m), Band(100m, 1m)), "Bands[2].From")
            .SetName("Create_BandsNotAscending_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(0m, 3m), Band(100.001m, 2m)), "Bands[1].From")
            .SetName("Create_BandFromWithTooManyDecimals_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(0m, 100.5m)), "Bands[0].RatePercent").SetName("Create_RateAbove100_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(0m, -1m)), "Bands[0].RatePercent").SetName("Create_NegativeRate_Returns400");
        yield return new TestCaseData(ScheduleBody("USD", day, Band(0m, 3.12345m)), "Bands[0].RatePercent")
            .SetName("Create_RateWithFiveDecimals_Returns400");
        yield return new TestCaseData(ScheduleBody("ZZZ", day), "Currency").SetName("Create_UnknownCurrency_Returns400");
        yield return new TestCaseData(new Dictionary<string, object?> { ["currency"] = "USD", ["bands"] = new[] { Band(0m, 3m) } }, "EffectiveFrom")
            .SetName("Create_WithoutEffectiveFrom_Returns400");
    }

    private static Dictionary<string, object?> Set(Dictionary<string, object?> body, string key, object? value)
    {
        body[key] = value;
        return body;
    }

    [TestCaseSource(nameof(InvalidShapes))]
    public async Task Create_AnInvalidShape_Returns400_AtTheField(Dictionary<string, object?> body, string field)
    {
        var world = await NominatedWorld.CreateAsync();

        var response = await Send(HttpMethod.Post, Schedules, world.TenantAdmin.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain(field);
        (await CountSchedulesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Update_APendingRow_Returns204_AndReplacesTheDateAndBands()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateScheduleAsync(token, "USD", Today.AddDays(10), Band(0m, 3m), Band(100000m, 2m));

        var response = await Send(HttpMethod.Put, $"{Schedules}/{id}", token,
            UpdateBody(Today.AddDays(12), await RowVersionAsync(token, id), Band(0m, 2.5m)));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await GetAsync(token, $"{Schedules}/{id}");
        item.GetProperty("effectiveFrom").GetString().ShouldBe(Today.AddDays(12).ToString("yyyy-MM-dd"));
        item.GetProperty("bands").GetArrayLength().ShouldBe(1);
        item.GetProperty("bands")[0].GetProperty("ratePercent").GetDecimal().ShouldBe(2.5m);
        item.GetProperty("currency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task Update_ACurrencyInTheBody_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateScheduleAsync(token, "USD", Today.AddDays(10));
        var body = UpdateBody(Today.AddDays(12), await RowVersionAsync(token, id));
        body["currency"] = "NZD";

        (await Send(HttpMethod.Put, $"{Schedules}/{id}", token, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(token, $"{Schedules}/{id}")).GetProperty("currency").GetString().ShouldBe("USD");
    }

    [Test]
    public async Task Update_AStaleRowVersion_Returns409StaleRowVersion()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));

        var response = await Send(HttpMethod.Put, $"{Schedules}/{id}", world.TenantAdmin.AccessToken,
            UpdateBody(Today.AddDays(12), StaleRowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task Update_ANewDateNotAfterTheCutOff_OrOntoAnotherRowsDate_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateScheduleAsync(token, "USD", Today.AddDays(10));
        await CreateScheduleAsync(token, "USD", Today.AddDays(20));

        foreach (var day in new[] { Today, Today.AddDays(20) })
        {
            var response = await Send(HttpMethod.Put, $"{Schedules}/{id}", token, UpdateBody(day, await RowVersionAsync(token, id)));
            (await CustodialHttp.ErrorsAsync(response)).Keys.ShouldContain("EffectiveFrom");
        }
    }

    [Test]
    public async Task Delete_APendingRow_Returns204_AndTheRowIsGone()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));

        (await Send(HttpMethod.Delete, $"{Schedules}/{id}", world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Get, $"{Schedules}/{id}", world.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountSchedulesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AfterAnAccountWasAccruedThroughALaterDay_ARowOnOrBeforeThatDay_IsInEffect()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateScheduleAsync(token, "USD", Today.AddDays(10));
        var nzd = await CreateScheduleAsync(token, "NZD", Today.AddDays(10));
        var account = await OpenCustodialAsync(world.Tenant.Id, world.AdviserToken.AccessToken, world.Customer.PublicId);
        await SetAccruedThroughAsync(account, Today.AddDays(15));

        (await GetAsync(token, $"{Schedules}/{id}")).GetProperty("inEffect").GetBoolean().ShouldBeTrue();
        (await GetAsync(token, $"{Schedules}/{nzd}")).GetProperty("inEffect").GetBoolean().ShouldBeFalse();

        var create = await Send(HttpMethod.Post, Schedules, token, ScheduleBody("USD", Today.AddDays(15)));
        (await CustodialHttp.ErrorsAsync(create)).Keys.ShouldContain("EffectiveFrom");
        await CreateScheduleAsync(token, "USD", Today.AddDays(16));

        var update = await Send(HttpMethod.Put, $"{Schedules}/{id}", token, UpdateBody(Today.AddDays(30), await RowVersionAsync(token, id)));
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(update)).Code.ShouldBe("interest_schedule_in_effect");
        var delete = await Send(HttpMethod.Delete, $"{Schedules}/{id}", token);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(delete)).Code.ShouldBe("interest_schedule_in_effect");
        (await GetAsync(token, $"{Schedules}/{id}")).GetProperty("effectiveFrom").GetString().ShouldBe(Today.AddDays(10).ToString("yyyy-MM-dd"));
    }

    [Test]
    public async Task AnExternalAccountsAccruedThrough_DoesNotMoveTheCutOff()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = await CreateScheduleAsync(token, "USD", Today.AddDays(10));
        var external = await CustodialHttp.OpenExternalAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        await SetAccruedThroughAsync(external, Today.AddDays(15));

        (await GetAsync(token, $"{Schedules}/{id}")).GetProperty("inEffect").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task UnknownId_Returns404()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = Guid.NewGuid();

        (await Send(HttpMethod.Get, $"{Schedules}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, $"{Schedules}/{id}", token, UpdateBody(Today.AddDays(10), StaleRowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, $"{Schedules}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsSchedule_Returns404_AndIsNotListed()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var theirs = await CreateScheduleAsync(south.TenantAdmin.AccessToken, "USD", Today.AddDays(10));
        var ours = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));
        var token = world.TenantAdmin.AccessToken;

        (await Send(HttpMethod.Get, $"{Schedules}/{theirs}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, $"{Schedules}/{theirs}", token,
                UpdateBody(Today.AddDays(12), await RowVersionAsync(south.TenantAdmin.AccessToken, theirs))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, $"{Schedules}/{theirs}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Ids(await GetAsync(token, Schedules)).ShouldBe([ours]);
        (await GetAsync(south.TenantAdmin.AccessToken, $"{Schedules}/{theirs}"))
            .GetProperty("effectiveFrom").GetString().ShouldBe(Today.AddDays(10).ToString("yyyy-MM-dd"));
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsAccruedAccounts_DoNotMoveTheCutOff()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await CreateScheduleAsync(world.TenantAdmin.AccessToken, "USD", Today.AddDays(10));
        var theirs = await OpenCustodialAsync(south.Tenant.Id, south.AdviserToken.AccessToken, south.Customer.PublicId);
        await SetAccruedThroughAsync(theirs, Today.AddDays(15));

        (await GetAsync(world.TenantAdmin.AccessToken, $"{Schedules}/{id}")).GetProperty("inEffect").GetBoolean().ShouldBeFalse();
    }

    private static List<Guid> Ids(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private static IEnumerable<(HttpMethod Method, string Path, object? Body)> Routes(Guid id)
    {
        yield return (HttpMethod.Get, Schedules, null);
        yield return (HttpMethod.Get, $"{Schedules}/{id}", null);
        yield return (HttpMethod.Post, Schedules, ScheduleBody("USD", Today.AddDays(20)));
        yield return (HttpMethod.Put, $"{Schedules}/{id}", UpdateBody(Today.AddDays(21), StaleRowVersion));
        yield return (HttpMethod.Delete, $"{Schedules}/{id}", null);
    }
}
