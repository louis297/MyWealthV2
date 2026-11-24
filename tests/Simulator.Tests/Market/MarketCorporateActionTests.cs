using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Corporate-actions R39–R43, R46, R47: the simulator market announces, amends and cancels corporate actions
/// of its instruments, and makes one effective with the price rebased when the clock reaches its ex-date.</summary>
public class MarketCorporateActionTests
{
    private const int Seed = 42;
    private static readonly DateOnly Day0 = DateOnly.FromDateTime(TestClock.Start.UtcDateTime);

    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory();
        _control = _factory.SessionClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _control.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(TestClock.Start);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"UPDATE [World] SET [Seed] = {Seed};");
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
    }

    [Test]
    public async Task Script0009_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0009_market_corporate_actions.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task Announce_Returns201_WithTheWorldsActionId_VersionOne_AndLogsIt()
    {
        var world = await MarketHttp.WorldIdAsync();

        var response = await MarketHttp.AnnounceAsync(_control, " acme.sim ", exDate: Day0.AddDays(2), recordDate: Day0.AddDays(3));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = await response.ReadJsonAsync();
        var id = json.RootElement.GetProperty("id").GetString()!;
        id.ShouldBe($"{world:N}-CA1");
        response.Headers.Location!.ToString().ShouldBe($"/control/market/corporate-actions/{id}");
        var action = await MarketHttp.CorporateActionAsync(_control, id);
        action.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        action.GetProperty("currency").GetString().ShouldBe("USD");
        action.GetProperty("kind").GetString().ShouldBe("Split");
        (action.GetProperty("ratioNew").GetInt32(), action.GetProperty("ratioHeld").GetInt32()).ShouldBe((2, 1));
        action.GetProperty("exDate").GetString().ShouldBe(Day0.AddDays(2).ToString("yyyy-MM-dd"));
        action.GetProperty("recordDate").GetString().ShouldBe(Day0.AddDays(3).ToString("yyyy-MM-dd"));
        action.GetProperty("status").GetString().ShouldBe("Announced");
        action.GetProperty("version").GetInt32().ShouldBe(1);
        var version = action.GetProperty("versions").EnumerateArray().Single();
        (version.GetProperty("version").GetInt32(), version.GetProperty("status").GetString()).ShouldBe((1, "Announced"));
        (await LogActionsAsync()).ShouldBe(["CorporateActionAnnounced", "MarketInstrumentAdded"]);
        (await MarketHttp.AnnounceOkAsync(_control, kind: "Bonus", ratioNew: 1, ratioHeld: 5)).ShouldBe($"{world:N}-CA2");
    }

    [TestCase("NOPE.SIM", "Split", 2, 1, 2, "Symbol")]
    [TestCase("ACME.SIM", "Rights", 2, 1, 2, "Kind")]
    [TestCase("ACME.SIM", "Split", 1, 2, 2, "RatioNew")]
    [TestCase("ACME.SIM", "Consolidation", 2, 1, 2, "RatioNew")]
    [TestCase("ACME.SIM", "Bonus", 0, 1, 2, "RatioNew")]
    [TestCase("ACME.SIM", "Split", 10001, 1, 2, "RatioNew")]
    [TestCase("ACME.SIM", "Split", 2, 1, 0, "ExDate")]
    [TestCase("ACME.SIM", "Split", 2, 1, -1, "ExDate")]
    public async Task Announce_ABadField_Returns400(string symbol, string kind, int ratioNew, int ratioHeld, int exDays, string field)
    {
        var response = await MarketHttp.AnnounceAsync(_control, symbol, kind, ratioNew, ratioHeld, Day0.AddDays(exDays));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = await response.ReadJsonAsync();
        json.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(json.RootElement.ToString());
        (await LogActionsAsync()).ShouldBe(["MarketInstrumentAdded"]);
    }

    [Test]
    public async Task Amend_AppendsAVersionWithTheNewTerms_AndCancel_AVersionCancelled()
    {
        var id = await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2));

        var amend = await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend", JsonSerializer.Serialize(new
        {
            ratioNew = 3, ratioHeld = 1, exDate = Day0.AddDays(4).ToString("yyyy-MM-dd"), recordDate = (string?)null,
            paymentDate = Day0.AddDays(9).ToString("yyyy-MM-dd")
        }));

        amend.StatusCode.ShouldBe(HttpStatusCode.OK, await amend.Content.ReadAsStringAsync());
        using (var json = await amend.ReadJsonAsync())
        {
            json.RootElement.GetProperty("version").GetInt32().ShouldBe(2);
            json.RootElement.GetProperty("ratioNew").GetInt32().ShouldBe(3);
            json.RootElement.GetProperty("exDate").GetString().ShouldBe(Day0.AddDays(4).ToString("yyyy-MM-dd"));
            json.RootElement.GetProperty("kind").GetString().ShouldBe("Split");
        }

        var cancel = await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/cancel", "{}");
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var action = await MarketHttp.CorporateActionAsync(_control, id);
        action.GetProperty("status").GetString().ShouldBe("Cancelled");
        action.GetProperty("versions").EnumerateArray().Select(version => (version.GetProperty("version").GetInt32(), version.GetProperty("status").GetString()))
            .ShouldBe([(1, "Announced"), (2, "Announced"), (3, "Cancelled")]);
        (await LogActionsAsync()).Take(2).ShouldBe(["CorporateActionCancelled", "CorporateActionAmended"]);
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/cancel", "{}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Amend_WithARatioThatDoesNotFit_OrAnExDateNotAhead_Returns400()
    {
        var id = await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2));

        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend",
                JsonSerializer.Serialize(new { ratioNew = 1, ratioHeld = 2, exDate = Day0.AddDays(4).ToString("yyyy-MM-dd") })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend",
                JsonSerializer.Serialize(new { ratioNew = 3, ratioHeld = 1, exDate = Day0.ToString("yyyy-MM-dd") })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MarketHttp.CorporateActionAsync(_control, id)).GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task AdvancingTheClockToTheExDate_MakesItEffective_AppendsAVersion_AndHalvesThePrice()
    {
        var id = await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2));

        await MarketHttp.AdvanceAsync(_control, "P1D");
        (await MarketHttp.CorporateActionAsync(_control, id)).GetProperty("status").GetString().ShouldBe("Announced");
        await MarketHttp.AdvanceAsync(_control, "P2D");

        var action = await MarketHttp.CorporateActionAsync(_control, id);
        action.GetProperty("status").GetString().ShouldBe("Effective");
        action.GetProperty("versions").EnumerateArray().Last().GetProperty("status").GetString().ShouldBe("Effective");
        (await MarketHttp.PricesAsync(_control, "ACME.SIM")).Select(row => (row.Date, row.Price)).ShouldBe([
            (Day0.AddDays(3), 50m), (Day0.AddDays(2), 50m), (Day0.AddDays(1), 100m), (Day0, 100m)
        ]);
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend",
                JsonSerializer.Serialize(new { ratioNew = 3, ratioHeld = 1, exDate = Day0.AddDays(9).ToString("yyyy-MM-dd") })))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/cancel", "{}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("Bonus", 1, 4, 80)]
    [TestCase("Consolidation", 1, 10, 1000)]
    [TestCase("Split", 3, 2, 66.6667)]
    public async Task TheRebase_UsesTheRatioOfTheKind(string kind, int ratioNew, int ratioHeld, decimal expected)
    {
        await MarketHttp.AnnounceOkAsync(_control, kind: kind, ratioNew: ratioNew, ratioHeld: ratioHeld, exDate: Day0.AddDays(1));

        await MarketHttp.AdvanceAsync(_control, "P1D");

        (await MarketHttp.PricesAsync(_control, "ACME.SIM")).First().Price.ShouldBe(expected);
    }

    [Test]
    public async Task TheList_IsNewestFirst_AndFilters()
    {
        await MarketHttp.AddOkAsync(_control, "KIWI.SIM", "NZD", 5m, dailyMovePercent: 0m);
        var first = await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(1));
        var second = await MarketHttp.AnnounceOkAsync(_control, "KIWI.SIM", exDate: Day0.AddDays(5));
        await MarketHttp.AdvanceAsync(_control, "P1D");

        (await ListAsync("")).ShouldBe([second, first]);
        (await ListAsync("?symbol=kiwi.sim")).ShouldBe([second]);
        (await ListAsync("?status=Effective")).ShouldBe([first]);
        (await _control.GetAsync("/control/market/corporate-actions?status=Paid")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnUnknownId_OrOneOfAnotherWorld_Returns404()
    {
        await MarketHttp.AnnounceOkAsync(_control);

        (await _control.GetAsync($"/control/market/corporate-actions/{Guid.NewGuid():N}-CA1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _control.GetAsync("/control/market/corporate-actions/nonsense")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{Guid.NewGuid():N}-CA1/cancel", "{}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AReset_EmptiesTheActions_AndTheIdsDoNotRepeat()
    {
        var before = await MarketHttp.AnnounceOkAsync(_control);

        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MarketHttp.AddOkAsync(_control, "NEW.SIM", "USD", 10m, dailyMovePercent: 0m);
        var after = await MarketHttp.AnnounceOkAsync(_control, "NEW.SIM");

        after.ShouldNotBe(before);
        after.ShouldEndWith("-CA1");
        (await ListAsync("")).ShouldBe([after]);
    }

    private async Task<List<string>> ListAsync(string query)
    {
        var response = await _control.GetAsync($"/control/market/corporate-actions{query}");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
    }

    private static async Task<List<string>> LogActionsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT [Action] FROM [ControlLog] ORDER BY [Id] DESC");
        return rows.Select(row => (string)row[0]).ToList();
    }
}
