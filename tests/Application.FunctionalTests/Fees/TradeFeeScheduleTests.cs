using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>Trade fees R1–R13: <c>/fees/trade-schedules</c>.</summary>
public class TradeFeeScheduleTests : TestBase
{
    private const string Schedules = FeeHttp.Schedules;

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        foreach (var (method, path) in new[] { (HttpMethod.Get, Schedules), (HttpMethod.Post, Schedules) })
        {
            var response = await FeeHttp.Send(method, path, null, method == HttpMethod.Post ? new { quoteCurrency = "USD" } : null);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Test]
    public async Task Customer_Returns403OnEveryVerb()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var token = (await TestApp.IssueAccessTokenAsync(customer)).AccessToken;

        (await FeeHttp.Send(HttpMethod.Get, Schedules, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Post, Schedules, token, FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", token, UpdateBody(FeeHttp.Today.AddDays(11), "AAAA")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_ReadsButCannotWrite()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var adviser = (await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory")).AccessToken;
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        (await FeeHttp.Send(HttpMethod.Get, Schedules, adviser)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", adviser)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FeeHttp.Send(HttpMethod.Post, Schedules, adviser, FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(20))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", adviser, UpdateBody(FeeHttp.Today.AddDays(11), rowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", adviser)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_CreatesAndGetReturnsTheItem()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var effectiveFrom = FeeHttp.Today.AddDays(10);

        var response = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken, new
        {
            quoteCurrency = "usd",
            effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            minimum = 10.00m,
            cap = 250.00m,
            bands = new[] { new { from = 0m, ratePercent = 0.5m }, new { from = 10000.00m, ratePercent = 0.3m } }
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        created.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["id"]);
        var id = created.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"{Schedules}/{id}");

        using var item = await FeeHttp.GetScheduleAsync(admin.AccessToken, id);
        var root = item.RootElement;
        root.GetProperty("id").GetGuid().ShouldBe(id);
        root.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        root.GetProperty("effectiveFrom").GetString().ShouldBe(effectiveFrom.ToString("yyyy-MM-dd"));
        root.GetProperty("minimum").GetDecimal().ShouldBe(10m);
        root.GetProperty("cap").GetDecimal().ShouldBe(250m);
        var bands = root.GetProperty("bands").EnumerateArray().ToList();
        bands.Select(band => (band.GetProperty("from").GetDecimal(), band.GetProperty("ratePercent").GetDecimal()))
            .ShouldBe([(0m, 0.5m), (10000m, 0.3m)]);
        root.GetProperty("inEffect").GetBoolean().ShouldBeFalse();
        root.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
        root.TryGetProperty("tenantId", out _).ShouldBeFalse();
    }

    [Test]
    public async Task List_FiltersByCurrencyAndSortsByCurrencyThenDate()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var today = FeeHttp.Today;
        var usdLater = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", today.AddDays(20));
        var nzd = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "NZD", today.AddDays(30));
        var usdEarlier = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", today.AddDays(10));

        var all = await FeeHttp.Send(HttpMethod.Get, $"{Schedules}?pageSize=2", admin.AccessToken);
        all.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await all.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(3);
            json.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
            json.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(2);
            json.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())
                .ShouldBe([nzd, usdEarlier]);
        }

        var usd = await FeeHttp.Send(HttpMethod.Get, $"{Schedules}?quoteCurrency=usd", admin.AccessToken);
        usd.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await usd.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())
                .ShouldBe([usdEarlier, usdLater]);
        }
    }

    [Test]
    public async Task TenantAdmin_WithTenantId_Returns400OnCreateAndList()
    {
        var (tenant, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var create = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken,
            FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(10), tenant.PublicId));
        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(create)).ShouldContainKey("TenantId");

        var list = await FeeHttp.Send(HttpMethod.Get, $"{Schedules}?tenantId={tenant.PublicId}", admin.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(list)).ShouldContainKey("TenantId");
    }

    [Test]
    public async Task SystemAdmin_NeedsTenantIdOnCreateAndList_AndMayReadAcrossTenants()
    {
        var (tenant, _, _, _) = await AccountHttp.SignInTenantAdmin();
        var systemAdmin = (await AccountHttp.SignInSystemAdmin()).AccessToken;

        var withoutTenant = await FeeHttp.Send(HttpMethod.Post, Schedules, systemAdmin,
            FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(10)));
        withoutTenant.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(withoutTenant)).ShouldContainKey("TenantId");
        (await FeeHttp.Send(HttpMethod.Get, Schedules, systemAdmin)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var unknown = await FeeHttp.Send(HttpMethod.Post, Schedules, systemAdmin,
            FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(10), Guid.NewGuid()));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var id = await FeeHttp.CreateScheduleAsync(systemAdmin, "USD", FeeHttp.Today.AddDays(10), tenant.PublicId);
        using (var item = await FeeHttp.GetScheduleAsync(systemAdmin, id))
        {
            item.RootElement.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        }

        var list = await FeeHttp.Send(HttpMethod.Get, $"{Schedules}?tenantId={tenant.PublicId}", systemAdmin);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())
                .ShouldBe([id]);
        }

        var rowVersion = await FeeHttp.RowVersionAsync(systemAdmin, id);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", systemAdmin, UpdateBody(FeeHttp.Today.AddDays(12), rowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", systemAdmin)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Create_EffectiveFromTodayOrEarlier_Returns400_TomorrowIsAccepted()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        foreach (var date in new[] { FeeHttp.Today, FeeHttp.Today.AddDays(-3) })
        {
            var response = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken, FeeHttp.ScheduleBody("USD", date));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("EffectiveFrom");
        }

        await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(1));
    }

    [Test]
    public async Task Create_NotAfterAFutureDatedBuy_Returns400_OtherCurrenciesAreNotAffected()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        var tradeDate = FeeHttp.Today.AddDays(20);
        await FeeHttp.InsertBuyAsync(accountId, instrumentId, 2m, tradeDate);

        var onTradeDate = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken, FeeHttp.ScheduleBody("USD", tradeDate));
        onTradeDate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(onTradeDate)).ShouldContainKey("EffectiveFrom");

        await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", tradeDate.AddDays(1));
        await FeeHttp.CreateScheduleAsync(admin.AccessToken, "NZD", FeeHttp.Today.AddDays(1));
    }

    [Test]
    public async Task Create_DuplicateDate_Returns400()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));

        var response = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken, FeeHttp.ScheduleBody("USD", FeeHttp.Today.AddDays(10)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("EffectiveFrom");
    }

    private static IEnumerable<TestCaseData> InvalidShapes()
    {
        object Band(decimal from, decimal ratePercent) => new { from, ratePercent };
        object Body(string currency = "USD", decimal minimum = 10m, decimal? cap = null, object[]? bands = null) => new
        {
            quoteCurrency = currency,
            effectiveFrom = FeeHttp.Today.AddDays(10).ToString("yyyy-MM-dd"),
            minimum,
            cap,
            bands = bands ?? [Band(0m, 0.5m)]
        };

        yield return new TestCaseData(Body(bands: []), "Bands").SetName("Create_NoBands_Returns400");
        yield return new TestCaseData(
            Body(bands: Enumerable.Range(0, 11).Select(index => Band(index * 100m, 1m)).ToArray()), "Bands")
            .SetName("Create_ElevenBands_Returns400");
        yield return new TestCaseData(Body(bands: [Band(5m, 0.5m)]), "Bands[0].From").SetName("Create_FirstBandNotAtZero_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 0.5m), Band(100m, 0.4m), Band(100m, 0.3m)]), "Bands[2].From")
            .SetName("Create_BandsNotAscending_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 0.5m), Band(100.001m, 0.3m)]), "Bands[1].From")
            .SetName("Create_BandFromWithTooManyDecimals_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 100.5m)]), "Bands[0].RatePercent").SetName("Create_RateAbove100_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 0.12345m)]), "Bands[0].RatePercent")
            .SetName("Create_RateWithFiveDecimals_Returns400");
        yield return new TestCaseData(Body(minimum: -1m), "Minimum").SetName("Create_NegativeMinimum_Returns400");
        yield return new TestCaseData(Body(minimum: 10.005m), "Minimum").SetName("Create_MinimumWithTooManyDecimals_Returns400");
        yield return new TestCaseData(Body(cap: 5m), "Cap").SetName("Create_CapBelowMinimum_Returns400");
        yield return new TestCaseData(Body(minimum: 0m, cap: 0m), "Cap").SetName("Create_ZeroCap_Returns400");
        yield return new TestCaseData(Body(currency: "ZZZ"), "QuoteCurrency").SetName("Create_UnknownCurrency_Returns400");
        yield return new TestCaseData(new { quoteCurrency = "USD", minimum = 0m, bands = new[] { Band(0m, 0.5m) } }, "EffectiveFrom")
            .SetName("Create_WithoutEffectiveFrom_Returns400");
    }

    [TestCaseSource(nameof(InvalidShapes))]
    public async Task Create_InvalidShape_Returns400WithTheField(object body, string field)
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var response = await FeeHttp.Send(HttpMethod.Post, Schedules, admin.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey(field);
        (await CountSchedulesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Update_Pending_Returns204AndReplacesTheBands()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var response = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, new
        {
            effectiveFrom = FeeHttp.Today.AddDays(15).ToString("yyyy-MM-dd"),
            minimum = 5.00m,
            cap = 100.00m,
            bands = new[]
            {
                new { from = 0m, ratePercent = 0.8m },
                new { from = 5000.00m, ratePercent = 0.6m },
                new { from = 20000.00m, ratePercent = 0.2m }
            },
            rowVersion
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var item = await FeeHttp.GetScheduleAsync(admin.AccessToken, id);
        var root = item.RootElement;
        root.GetProperty("quoteCurrency").GetString().ShouldBe("USD");
        root.GetProperty("effectiveFrom").GetString().ShouldBe(FeeHttp.Today.AddDays(15).ToString("yyyy-MM-dd"));
        root.GetProperty("minimum").GetDecimal().ShouldBe(5m);
        root.GetProperty("cap").GetDecimal().ShouldBe(100m);
        root.GetProperty("bands").EnumerateArray()
            .Select(band => (band.GetProperty("from").GetDecimal(), band.GetProperty("ratePercent").GetDecimal()))
            .ShouldBe([(0m, 0.8m), (5000m, 0.6m), (20000m, 0.2m)]);
        root.GetProperty("rowVersion").GetString().ShouldNotBe(rowVersion);
    }

    [Test]
    public async Task Update_StaleRowVersion_Returns409StaleRowVersion()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(11), rowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(12), rowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("stale_row_version");
    }

    [Test]
    public async Task UpdateAndDelete_InEffect_Return409FeeScheduleInEffect()
    {
        var (_, _, customer, admin) = await AccountHttp.SignInTenantAdmin();
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(admin.AccessToken, customer.PublicId);
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(5));
        await FeeHttp.InsertBuyAsync(accountId, instrumentId, 2m, FeeHttp.Today.AddDays(6));
        using (var item = await FeeHttp.GetScheduleAsync(admin.AccessToken, id))
        {
            item.RootElement.GetProperty("inEffect").GetBoolean().ShouldBeTrue();
        }

        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var update = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(30), rowVersion));
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(update)).Code.ShouldBe("fee_schedule_in_effect");

        var delete = await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", admin.AccessToken);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(delete)).Code.ShouldBe("fee_schedule_in_effect");
        (await CountSchedulesAsync()).ShouldBe(1);
    }

    [Test]
    public async Task UpdateAndDelete_RowStartingToday_Return409FeeScheduleInEffect()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(1));
        await MoveEffectiveFromAsync(id, FeeHttp.Today);
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var update = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(30), rowVersion));
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemResponse.ReadAsync(update)).Code.ShouldBe("fee_schedule_in_effect");
        (await ProblemResponse.ReadAsync(await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", admin.AccessToken)))
            .Code.ShouldBe("fee_schedule_in_effect");
    }

    [Test]
    public async Task Update_NewEffectiveFromNotAfterTheCutOff_Returns400()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var response = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today, rowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("EffectiveFrom");
    }

    [Test]
    public async Task Update_OntoAnotherRowsDate_Returns400()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(20));
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var response = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(10), rowVersion));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("EffectiveFrom");
    }

    [Test]
    public async Task Update_InvalidShape_Returns400WithTheField()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var rowVersion = await FeeHttp.RowVersionAsync(admin.AccessToken, id);

        var response = await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, new
        {
            effectiveFrom = FeeHttp.Today.AddDays(10).ToString("yyyy-MM-dd"),
            minimum = 1.001m,
            bands = new[] { new { from = 0m, ratePercent = 0.5m } },
            rowVersion
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("Minimum");
    }

    [Test]
    public async Task Delete_Pending_Returns204AndTheRowIsGone()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));

        var response = await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", admin.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CountSchedulesAsync()).ShouldBe(0);
    }

    [Test]
    public async Task UnknownId_Returns404()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var known = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{known}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = Guid.NewGuid();

        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", admin.AccessToken, UpdateBody(FeeHttp.Today.AddDays(3), "AAAA")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task OtherTenantsSchedule_Returns404OnGetUpdateAndDelete()
    {
        var (_, _, _, north) = await AccountHttp.SignInTenantAdmin();
        var (_, _, _, south) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var id = await FeeHttp.CreateScheduleAsync(north.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        var rowVersion = await FeeHttp.RowVersionAsync(north.AccessToken, id);

        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", south.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FeeHttp.Send(HttpMethod.Put, $"{Schedules}/{id}", south.AccessToken, UpdateBody(FeeHttp.Today.AddDays(11), rowVersion)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", south.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var list = JsonDocument.Parse(await (await FeeHttp.Send(HttpMethod.Get, Schedules, south.AccessToken)).Content.ReadAsStringAsync());
        list.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await CountSchedulesAsync()).ShouldBe(1);
    }

    [Test]
    public async Task OtherTenantsTrades_DoNotMoveTheCutOff()
    {
        var (_, _, _, north) = await AccountHttp.SignInTenantAdmin();
        var (_, _, southCustomer, south) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example",
            adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var (accountId, instrumentId) = await FeeHttp.TradingAccountAsync(south.AccessToken, southCustomer.PublicId);
        await FeeHttp.InsertBuyAsync(accountId, instrumentId, 2m, FeeHttp.Today.AddDays(20));

        await FeeHttp.CreateScheduleAsync(north.AccessToken, "USD", FeeHttp.Today.AddDays(1));
    }

    [Test]
    public async Task DisabledTenant_Returns403()
    {
        var (tenant, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var id = await FeeHttp.CreateScheduleAsync(admin.AccessToken, "USD", FeeHttp.Today.AddDays(10));
        await DisableTenantAsync(tenant.Id);

        (await FeeHttp.Send(HttpMethod.Get, Schedules, admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Get, $"{Schedules}/{id}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeeHttp.Send(HttpMethod.Delete, $"{Schedules}/{id}", admin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static object UpdateBody(DateOnly effectiveFrom, string rowVersion) => new
    {
        effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
        minimum = 0m,
        cap = (decimal?)null,
        bands = new[] { new { from = 0m, ratePercent = 0.25m } },
        rowVersion
    };

    private static async Task<int> CountSchedulesAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [TradeFeeSchedules]").SingleAsync();
    }

    private static async Task MoveEffectiveFromAsync(Guid id, DateOnly effectiveFrom)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [TradeFeeSchedules] SET [EffectiveFrom] = {0} WHERE [PublicId] = {1}", effectiveFrom, id);
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
