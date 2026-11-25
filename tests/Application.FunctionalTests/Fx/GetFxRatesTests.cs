using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Fx;

/// <summary>Market data history R20–R22: <c>GET /fx-rates</c>. Platform data, so no isolation test (fx-rates §10).</summary>
[Parallelizable]
public class GetFxRatesTests : TestBase
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 26, 11, 30, 2, TimeSpan.Zero);

    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await FunctionalTestSetup.WebClient.GetAsync("/fx-rates?base=USD&quote=NZD");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task EveryRole_Returns200()
    {
        await AddNewYearRatesAsync();
        var tenant = await TestApp.CreateTenantAsync("Firm R", "firmr");
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", "tess@firmr", "Password1!", tenant.Id);
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firmr", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(UserRole.Customer, "Dee", "dee@firmr", "Password1!", tenant.Id, adviser.Id);
        var tokens = new[]
        {
            (await FunctionalTestSetup.BackOfficeOidc.SignInAsync(
                DevelopmentIdentitySeeder.SystemAdminEmail, DevelopmentIdentitySeeder.SystemAdminPassword, tenantCode: null)).AccessToken,
            (await FunctionalTestSetup.Oidc.SignInAsync("tess@firmr", "Password1!", "firmr")).AccessToken,
            (await FunctionalTestSetup.Oidc.SignInAsync("ann@firmr", "Password1!", "firmr")).AccessToken,
            (await TestApp.IssueAccessTokenAsync(customer)).AccessToken
        };

        foreach (var token in tokens)
        {
            var page = await ReadAsync(token, "/fx-rates?base=USD&quote=NZD&from=2026-01-01&to=2026-01-05");
            page.GetProperty("totalCount").GetInt32().ShouldBe(5);
        }
    }

    [Test]
    public async Task ReadsThePair_NewestFirst_WithEveryField()
    {
        await AddNewYearRatesAsync();
        var token = await AdviserTokenAsync();

        var page = await ReadAsync(token, "/fx-rates?base=usd&quote=nzd&from=2026-01-01&to=2026-01-05");

        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        page.GetProperty("totalCount").GetInt32().ShouldBe(5);
        Dates(page).ShouldBe(["2026-01-05", "2026-01-04", "2026-01-03", "2026-01-02", "2026-01-01"]);
        var first = page.GetProperty("items")[0];
        first.GetProperty("base").GetString().ShouldBe("USD");
        first.GetProperty("quote").GetString().ShouldBe("NZD");
        first.GetProperty("rate").GetDecimal().ShouldBe(1.735m);
        first.GetProperty("source").GetString().ShouldBe("Frankfurter");
        first.GetProperty("fetchedAt").GetDateTimeOffset().ShouldBe(Fetched);
    }

    /// <summary>§8.1 and R22: the weekend has a row of its own, and to with pageSize=1 answers it.</summary>
    [Test]
    public async Task ToWithPageSizeOne_AnswersTheRowOfThatDate()
    {
        await AddNewYearRatesAsync();
        var token = await AdviserTokenAsync();

        var page = await ReadAsync(token, "/fx-rates?base=USD&quote=NZD&from=2026-01-01&to=2026-01-04&pageSize=1");

        Dates(page).ShouldBe(["2026-01-04"]);
        page.GetProperty("items")[0].GetProperty("rate").GetDecimal().ShouldBe(1.732m);
        page.GetProperty("totalCount").GetInt32().ShouldBe(4);
    }

    [Test]
    public async Task ThePairIsDirected_TheInverseRowsAreNotAnswered()
    {
        await AddNewYearRatesAsync();
        await AddRateAsync("NZD", "USD", new DateOnly(2025, 6, 2), 0.6m);
        var token = await AdviserTokenAsync();

        var direct = await ReadAsync(token, "/fx-rates?base=USD&quote=NZD&from=2025-06-01&to=2025-06-30");
        direct.GetProperty("items").GetArrayLength().ShouldBe(0);
        direct.GetProperty("totalCount").GetInt32().ShouldBe(0);

        var inverse = await ReadAsync(token, "/fx-rates?base=NZD&quote=USD&from=2025-06-01&to=2025-06-30");
        Dates(inverse).ShouldBe(["2025-06-02"]);
        inverse.GetProperty("items")[0].GetProperty("rate").GetDecimal().ShouldBe(0.6m);
    }

    [Test]
    public async Task Paging_ReadsTheSecondPage()
    {
        await AddNewYearRatesAsync();
        var token = await AdviserTokenAsync();

        var page = await ReadAsync(token, "/fx-rates?base=USD&quote=NZD&from=2026-01-01&to=2026-01-05&page=2&pageSize=2");

        Dates(page).ShouldBe(["2026-01-03", "2026-01-02"]);
    }

    [TestCase("quote=NZD", "Base")]
    [TestCase("base=&quote=NZD", "Base")]
    [TestCase("base=US&quote=NZD", "Base")]
    [TestCase("base=CHF&quote=NZD", "Base")]
    [TestCase("base=USD", "Quote")]
    [TestCase("base=USD&quote=NZDX", "Quote")]
    [TestCase("base=USD&quote=XYZ", "Quote")]
    [TestCase("base=USD&quote=usd", "Quote")]
    [TestCase("base=USD&quote=NZD&from=2026-02-30", "From")]
    [TestCase("base=USD&quote=NZD&to=yesterday", "To")]
    [TestCase("base=USD&quote=NZD&from=2026-01-05&to=2026-01-01", "From")]
    [TestCase("base=USD&quote=NZD&pageSize=101", "PageSize")]
    [TestCase("base=USD&quote=NZD&page=0", "Page")]
    public async Task ABadQuery_Returns400(string query, string field)
    {
        var token = await AdviserTokenAsync();

        var response = await Send(token, $"/fx-rates?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Test]
    public async Task TheSameCodeTwice_Returns400WithTheMessage()
    {
        var token = await AdviserTokenAsync();

        var response = await Send(token, "/fx-rates?base=NZD&quote=nzd");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").GetProperty("Quote").EnumerateArray().Select(item => item.GetString())
            .ShouldContain("No rate is stored for a currency against itself.");
    }

    private static List<string> Dates(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("date").GetString()!).ToList();

    private static async Task<string> AdviserTokenAsync()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm F", "firmf");
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann", "ann@firmf", "Password1!", tenant.Id);
        return (await FunctionalTestSetup.Oidc.SignInAsync("ann@firmf", "Password1!", "firmf")).AccessToken;
    }

    private static async Task<HttpResponseMessage> Send(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await FunctionalTestSetup.WebClient.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(string token, string path)
    {
        var response = await Send(token, path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>§1.1: USD → NZD from Frankfurter v2 over New Year 2026, a rate for every calendar day.</summary>
    private static async Task AddNewYearRatesAsync()
    {
        foreach (var (day, rate) in new[] { (1, 1.7294m), (2, 1.7313m), (3, 1.7315m), (4, 1.7320m), (5, 1.7350m) })
        {
            await AddRateAsync("USD", "NZD", new DateOnly(2026, 1, day), rate);
        }
    }

    private static async Task AddRateAsync(string baseCurrency, string quoteCurrency, DateOnly date, decimal rate)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = await db.FxRates.SingleOrDefaultAsync(row =>
            row.BaseCurrency == baseCurrency && row.QuoteCurrency == quoteCurrency && row.AsOfDate == date);
        if (existing is null)
        {
            existing = new FxRate { BaseCurrency = baseCurrency, QuoteCurrency = quoteCurrency, AsOfDate = date };
            db.FxRates.Add(existing);
        }

        existing.Rate = rate;
        existing.Source = "Frankfurter";
        existing.FetchedAt = Fetched;
        await db.SaveChangesAsync();
    }
}
