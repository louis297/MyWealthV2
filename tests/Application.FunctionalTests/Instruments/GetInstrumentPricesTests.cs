using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests.Instruments;

/// <summary>Market data history R19, R21–R23: <c>GET /instruments/{id}/prices</c>.</summary>
[Parallelizable]
public class GetInstrumentPricesTests : TestBase
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 26, 12, 0, 4, TimeSpan.Zero);

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{Guid.NewGuid()}/prices", accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);

        (await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{Guid.NewGuid()}/prices", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TenantAdmin_ReadsItsInstrument_NewestFirst()
    {
        var (_, tokens) = await InstrumentHttp.SignInTenantAdmin();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        var response = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}/prices", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("pageSize").GetInt32().ShouldBe(20);
        root.GetProperty("totalCount").GetInt32().ShouldBe(5);
        Dates(root).ShouldBe(["2026-01-05", "2026-01-02", "2025-12-31", "2025-12-30", "2025-12-29"]);
        var first = root.GetProperty("items")[0];
        first.GetProperty("price").GetDecimal().ShouldBe(267.26m);
        first.GetProperty("currency").GetString().ShouldBe("USD");
        first.GetProperty("source").GetString().ShouldBe("EODHD");
        first.GetProperty("fetchedAt").GetDateTimeOffset().ShouldBe(Fetched);
    }

    [Test]
    public async Task Adviser_ReadsWithFrom_To_AndBoth()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?from=2026-01-02"))
            .ShouldBe(["2026-01-05", "2026-01-02"]);
        Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?to=2025-12-30"))
            .ShouldBe(["2025-12-30", "2025-12-29"]);
        Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?from=2025-12-30&to=2026-01-02"))
            .ShouldBe(["2026-01-02", "2025-12-31", "2025-12-30"]);
    }

    /// <summary>R22 and §1.1: the weekend answers Friday's row; New Year's Day answers 31 December.</summary>
    [Test]
    public async Task ToWithPageSizeOne_AnswersTheLatestRowOnOrBeforeIt()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        var weekend = await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?to=2026-01-04&pageSize=1");
        Dates(weekend).ShouldBe(["2026-01-02"]);
        weekend.GetProperty("items")[0].GetProperty("price").GetDecimal().ShouldBe(271.01m);
        weekend.GetProperty("totalCount").GetInt32().ShouldBe(4);

        Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?to=2026-01-01&pageSize=1"))
            .ShouldBe(["2025-12-31"]);
    }

    [Test]
    public async Task ADateBeforeTheFirstRow_IsAnEmptyPage()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        var page = await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?to=2025-12-28&pageSize=1");

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Paging_ReadsTheSecondPage()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        var page = await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices?page=2&pageSize=2");

        Dates(page).ShouldBe(["2025-12-31", "2025-12-30"]);
        page.GetProperty("page").GetInt32().ShouldBe(2);
        page.GetProperty("totalCount").GetInt32().ShouldBe(5);
    }

    [Test]
    public async Task AnInactiveInstrument_IsReadable()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == id);
            instrument.Disable();
            await db.SaveChangesAsync();
        }

        Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{id}/prices")).Count.ShouldBe(5);
    }

    [TestCase("from=not-a-date", "From")]
    [TestCase("from=2026-1-2", "From")]
    [TestCase("to=2026-13-01", "To")]
    [TestCase("to=01/02/2026", "To")]
    [TestCase("from=2026-01-05&to=2026-01-02", "From")]
    [TestCase("pageSize=0", "PageSize")]
    [TestCase("pageSize=101", "PageSize")]
    [TestCase("page=0", "Page")]
    public async Task ABadQuery_Returns400(string query, string field)
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(tokens.AccessToken);

        var response = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{id}/prices?{query}", tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Test]
    public async Task AMissingId_Returns404()
    {
        var (_, tokens) = await InstrumentHttp.SignInAdviser();

        (await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{Guid.NewGuid()}/prices", tokens.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SystemAdmin_ReadsAnInstrumentOfAnyTenant()
    {
        var (_, adviserTokens) = await InstrumentHttp.SignInAdviser();
        var id = await CreateWithHistoryAsync(adviserTokens.AccessToken);
        var adminTokens = await InstrumentHttp.SignInSystemAdmin();

        Dates(await ReadAsync(adminTokens.AccessToken, $"/instruments/{id}/prices")).Count.ShouldBe(5);
    }

    /// <summary>Isolation: another Tenant's instrument is 404, also when both Tenants list the same symbol (R23).</summary>
    [Test]
    public async Task TenantAdminAndAdviserOfA_WithTheIdOfBsInstrument_Return404()
    {
        var (north, adminA) = await InstrumentHttp.SignInTenantAdmin("North Advisory", "north-advisory", "tess@north.example");
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann Adviser", "ann@north.example", "Password1!", north.Id);
        var adviserA = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory");
        var (_, adviserB) = await InstrumentHttp.SignInAdviser("South Co", "south-co", "bob@south.example");
        var idA = await CreateWithHistoryAsync(adminA.AccessToken);
        var idB = await CreateWithHistoryAsync(adviserB.AccessToken);

        foreach (var tokens in new[] { adminA, adviserA })
        {
            (await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{idB}/prices", tokens.AccessToken))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
            Dates(await ReadAsync(tokens.AccessToken, $"/instruments/{idA}/prices")).Count.ShouldBe(5);
        }

        Dates(await ReadAsync(adviserB.AccessToken, $"/instruments/{idB}/prices")).Count.ShouldBe(5);
    }

    private static List<string> Dates(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("date").GetString()!).ToList();

    private static async Task<JsonElement> ReadAsync(string accessToken, string path)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>Creates AAPL (USD) in the caller's Tenant with the five bars of spec §8.1.</summary>
    private static async Task<Guid> CreateWithHistoryAsync(string accessToken)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol = "AAPL",
            name = "Apple Inc.",
            quoteCurrency = "USD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == id);
        foreach (var (date, price) in new[]
                 {
                     (new DateOnly(2025, 12, 29), 273.76m),
                     (new DateOnly(2025, 12, 30), 273.08m),
                     (new DateOnly(2025, 12, 31), 271.86m),
                     (new DateOnly(2026, 1, 2), 271.01m),
                     (new DateOnly(2026, 1, 5), 267.26m)
                 })
        {
            db.InstrumentPrices.Add(new InstrumentPrice
            {
                InstrumentId = instrument.Id,
                AsOfDate = date,
                Price = price,
                Source = "EODHD",
                FetchedAt = Fetched
            });
        }

        await db.SaveChangesAsync();
        return id;
    }
}
