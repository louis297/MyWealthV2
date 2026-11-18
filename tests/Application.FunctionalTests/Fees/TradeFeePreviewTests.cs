using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Fees;

/// <summary>Trade fees amendment 2026-11-15, R30–R33: <c>POST /fees/trade-schedules/preview</c>.</summary>
[Parallelizable]
public class TradeFeePreviewTests : TestBase
{
    private const string Preview = FeeHttp.Schedules + "/preview";

    private static object Band(decimal from, decimal ratePercent) => new { from, ratePercent };

    private static object Body(
        string quoteCurrency = "NZD",
        decimal minimum = 10.00m,
        decimal? cap = null,
        object[]? bands = null,
        decimal[]? amounts = null) => new
    {
        quoteCurrency,
        minimum,
        cap,
        bands = bands ?? [Band(0m, 0.5m), Band(10000.00m, 0.3m)],
        amounts = amounts ?? [1000.00m, 15000.00m]
    };

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FeeHttp.Send(HttpMethod.Post, Preview, null, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var (_, _, customer, _) = await AccountHttp.SignInTenantAdmin();
        var token = (await TestApp.IssueAccessTokenAsync(customer)).AccessToken;

        (await FeeHttp.Send(HttpMethod.Post, Preview, token, Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adviser_Returns200WithEachAmountsCalculation()
    {
        var (_, _, _, adviser) = await AccountHttp.SignInAdviser();

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, adviser.AccessToken, Body());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("currency").GetString().ShouldBe("NZD");
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);

        var small = items[0];
        small.GetProperty("amount").GetDecimal().ShouldBe(1000m);
        small.GetProperty("bands").EnumerateArray().Select(Slice).ShouldBe([(0m, 0.5m, 1000m, 5m)]);
        small.GetProperty("banded").GetDecimal().ShouldBe(5m);
        small.GetProperty("limitApplied").GetString().ShouldBe("minimum");
        small.GetProperty("fee").GetDecimal().ShouldBe(10m);

        var large = items[1];
        large.GetProperty("amount").GetDecimal().ShouldBe(15000m);
        large.GetProperty("bands").EnumerateArray().Select(Slice)
            .ShouldBe([(0m, 0.5m, 10000m, 50m), (10000m, 0.3m, 5000m, 15m)]);
        large.GetProperty("banded").GetDecimal().ShouldBe(65m);
        large.GetProperty("limitApplied").ValueKind.ShouldBe(JsonValueKind.Null);
        large.GetProperty("fee").GetDecimal().ShouldBe(65m);
    }

    [Test]
    public async Task TenantAdmin_CapApplied_AndOnlyTheFeeIsRounded()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, admin.AccessToken,
            Body(quoteCurrency: "USD", minimum: 0m, cap: 100m, bands: [Band(0m, 0.5m)], amounts: [100000m, 1234.5678m]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items[0].GetProperty("banded").GetDecimal().ShouldBe(500m);
        items[0].GetProperty("limitApplied").GetString().ShouldBe("cap");
        items[0].GetProperty("fee").GetDecimal().ShouldBe(100m);
        items[1].GetProperty("bands")[0].GetProperty("fee").GetDecimal().ShouldBe(6.172839m);
        items[1].GetProperty("banded").GetDecimal().ShouldBe(6.172839m);
        items[1].GetProperty("fee").GetDecimal().ShouldBe(6.17m);
    }

    [Test]
    public async Task SystemAdmin_Returns200WithoutTenantId()
    {
        var systemAdmin = (await AccountHttp.SignInSystemAdmin()).AccessToken;

        (await FeeHttp.Send(HttpMethod.Post, Preview, systemAdmin, Body())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task TenantIdInTheBody_Returns400ForEveryCaller()
    {
        var (tenant, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        var systemAdmin = (await AccountHttp.SignInSystemAdmin()).AccessToken;
        var body = new
        {
            tenantId = tenant.PublicId,
            quoteCurrency = "NZD",
            minimum = 0m,
            bands = new[] { Band(0m, 0.5m) },
            amounts = new[] { 100m }
        };

        foreach (var token in new[] { admin.AccessToken, systemAdmin })
        {
            var response = await FeeHttp.Send(HttpMethod.Post, Preview, token, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await FeeHttp.ErrorsAsync(response)).ShouldContainKey("TenantId");
        }
    }

    private static IEnumerable<TestCaseData> InvalidSchedules()
    {
        yield return new TestCaseData(Body(bands: []), "Bands").SetName("Preview_NoBands_Returns400");
        yield return new TestCaseData(Body(bands: [Band(5m, 0.5m)]), "Bands[0].From").SetName("Preview_FirstBandNotAtZero_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 0.5m), Band(0m, 0.3m)]), "Bands[1].From")
            .SetName("Preview_BandsNotAscending_Returns400");
        yield return new TestCaseData(Body(bands: [Band(0m, 101m)]), "Bands[0].RatePercent").SetName("Preview_RateAbove100_Returns400");
        yield return new TestCaseData(Body(minimum: -1m), "Minimum").SetName("Preview_NegativeMinimum_Returns400");
        yield return new TestCaseData(Body(cap: 5m), "Cap").SetName("Preview_CapBelowMinimum_Returns400");
        yield return new TestCaseData(Body(quoteCurrency: "ZZZ"), "QuoteCurrency").SetName("Preview_UnknownCurrency_Returns400");
    }

    [TestCaseSource(nameof(InvalidSchedules))]
    public async Task InvalidSchedule_Returns400WithTheCreateFieldNames(object body, string field)
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, admin.AccessToken, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).ShouldContainKey(field);
    }

    private static IEnumerable<TestCaseData> InvalidAmounts()
    {
        yield return new TestCaseData((object)Array.Empty<decimal>()).SetName("Preview_NoAmounts_Returns400");
        yield return new TestCaseData((object)new[] { 1m, 2m, 3m, 4m, 5m, 6m }).SetName("Preview_SixAmounts_Returns400");
        yield return new TestCaseData((object)new[] { 0m }).SetName("Preview_ZeroAmount_Returns400");
        yield return new TestCaseData((object)new[] { 100m, -5m }).SetName("Preview_NegativeAmount_Returns400");
        yield return new TestCaseData((object)new[] { 100.12345m }).SetName("Preview_AmountWithFiveDecimals_Returns400");
    }

    [TestCaseSource(nameof(InvalidAmounts))]
    public async Task InvalidAmounts_Return400(decimal[] amounts)
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, admin.AccessToken, Body(amounts: amounts));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await FeeHttp.ErrorsAsync(response)).Keys
            .ShouldContain(key => key.StartsWith("Amounts", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task FiveAmounts_AreAccepted_AndNoRowIsWritten()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, admin.AccessToken,
            Body(amounts: [1m, 10m, 100m, 1000m, 10000.1234m]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(5);
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.TradeFeeSchedules.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await db.TradeFeeBands.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task PreviewIgnoresTheCutOffAndStoredSchedules()
    {
        var (_, _, _, admin) = await AccountHttp.SignInTenantAdmin();
        await FeeHttp.CreateScheduleAsync(admin.AccessToken, new
        {
            quoteCurrency = "NZD",
            effectiveFrom = FeeHttp.Today.AddDays(1).ToString("yyyy-MM-dd"),
            minimum = 50m,
            bands = new[] { Band(0m, 2m) }
        });

        var response = await FeeHttp.Send(HttpMethod.Post, Preview, admin.AccessToken,
            Body(minimum: 0m, bands: [Band(0m, 1m)], amounts: [100m]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items")[0].GetProperty("fee").GetDecimal().ShouldBe(1m);
    }

    private static (decimal From, decimal Rate, decimal Slice, decimal Fee) Slice(JsonElement band) =>
    (
        band.GetProperty("from").GetDecimal(),
        band.GetProperty("ratePercent").GetDecimal(),
        band.GetProperty("slice").GetDecimal(),
        band.GetProperty("fee").GetDecimal()
    );
}
