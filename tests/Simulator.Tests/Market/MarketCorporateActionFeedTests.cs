using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Corporate-actions R44, R45: <c>GET /party/market/corporate-actions</c>, the feed the platform reads.</summary>
public class MarketCorporateActionFeedTests
{
    private const string Path = "/party/market/corporate-actions";
    private static readonly DateOnly Day0 = DateOnly.FromDateTime(TestClock.Start.UtcDateTime);

    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;
    private HttpClient _party = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory();
        _control = _factory.SessionClient();
        _party = _factory.PartyClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _party.Dispose();
        _control.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public async Task ResetState()
    {
        _factory.Clock.Set(TestClock.Start);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
    }

    [Test]
    public async Task TheFeed_ListsEveryVersion_OldestFirst_WithTheWireFields()
    {
        var world = await MarketHttp.WorldIdAsync();
        var id = await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2));
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{id}/amend",
                JsonSerializer.Serialize(new { ratioNew = 3, ratioHeld = 1, exDate = Day0.AddDays(2).ToString("yyyy-MM-dd") })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await FeedAsync();

        page.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["items", "cursor", "hasMore"]);
        page.RootElement.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        var first = items[0];
        first.EnumerateObject().Select(property => property.Name).ShouldBe([
            "id", "actionId", "version", "status", "symbol", "currency", "kind", "ratioNew", "ratioHeld", "exDate", "recordDate", "paymentDate",
            "amountPerShare", "taxSource", "imputationCreditPerShare", "foreignTaxRatePercent", "reinvestmentPrice"
        ]);
        first.GetProperty("id").GetString().ShouldBe($"{world:N}-CA1-V1");
        first.GetProperty("actionId").GetString().ShouldBe(id);
        first.GetProperty("version").GetInt32().ShouldBe(1);
        first.GetProperty("status").GetString().ShouldBe("Announced");
        first.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        first.GetProperty("currency").GetString().ShouldBe("USD");
        first.GetProperty("kind").GetString().ShouldBe("Split");
        first.GetProperty("ratioNew").ValueKind.ShouldBe(JsonValueKind.Number);
        (first.GetProperty("ratioNew").GetInt32(), first.GetProperty("ratioHeld").GetInt32()).ShouldBe((2, 1));
        first.GetProperty("exDate").GetString().ShouldBe(Day0.AddDays(2).ToString("yyyy-MM-dd"));
        first.GetProperty("recordDate").ValueKind.ShouldBe(JsonValueKind.Null);
        items[1].GetProperty("id").GetString().ShouldBe($"{world:N}-CA1-V2");
        items[1].GetProperty("ratioNew").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task AClockAdvance_PutsTheEffectiveVersionOnTheFeed()
    {
        await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(1));
        var before = await FeedAsync();
        await MarketHttp.AdvanceAsync(_control, "P1D");

        var after = await FeedAsync(before.RootElement.GetProperty("cursor").GetString());

        var item = after.RootElement.GetProperty("items").EnumerateArray().Single();
        (item.GetProperty("version").GetInt32(), item.GetProperty("status").GetString()).ShouldBe((2, "Effective"));
    }

    [Test]
    public async Task TheFeed_ReadsAfterTheCursor_AndPages()
    {
        for (var i = 0; i < 5; i++)
        {
            await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2 + i));
        }

        var first = await FeedAsync(limit: 2);
        var second = await FeedAsync(first.RootElement.GetProperty("cursor").GetString(), limit: 2);
        var third = await FeedAsync(second.RootElement.GetProperty("cursor").GetString(), limit: 2);
        var empty = await FeedAsync(third.RootElement.GetProperty("cursor").GetString());

        Numbers(first).ShouldBe(["CA1", "CA2"]);
        first.RootElement.GetProperty("hasMore").GetBoolean().ShouldBeTrue();
        Numbers(second).ShouldBe(["CA3", "CA4"]);
        Numbers(third).ShouldBe(["CA5"]);
        third.RootElement.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        Numbers(empty).ShouldBeEmpty();
        empty.RootElement.GetProperty("cursor").GetString().ShouldBe(third.RootElement.GetProperty("cursor").GetString());
    }

    [TestCase(0, 1)]
    [TestCase(500, 100)]
    public async Task TheLimit_IsClamped(int limit, int expected)
    {
        for (var i = 0; i < 3; i++)
        {
            await MarketHttp.AnnounceOkAsync(_control, exDate: Day0.AddDays(2 + i));
        }

        Numbers(await FeedAsync(limit: limit)).Count.ShouldBe(Math.Min(expected, 3));
    }

    [Test]
    public async Task AnEmptyFeed_GivesACursorThatReadsLaterVersions()
    {
        var empty = await FeedAsync();
        await MarketHttp.AnnounceOkAsync(_control);

        Numbers(empty).ShouldBeEmpty();
        Numbers(await FeedAsync(empty.RootElement.GetProperty("cursor").GetString())).ShouldBe(["CA1"]);
    }

    [Test]
    public async Task ACursorFromAnEarlierWorld_ReadsFromTheStartOfTheCurrentWorld()
    {
        await MarketHttp.AnnounceOkAsync(_control);
        var old = await FeedAsync();
        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MarketHttp.AddOkAsync(_control, "NEW.SIM", "USD", 10m, dailyMovePercent: 0m);
        var fresh = await MarketHttp.AnnounceOkAsync(_control, "NEW.SIM");

        var page = await FeedAsync(old.RootElement.GetProperty("cursor").GetString());

        page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("actionId").GetString()).ShouldBe([fresh]);
    }

    [TestCase("garbage")]
    [TestCase("00000000000000000000000000000000:abc")]
    public async Task ACursorThatCannotBeRead_Returns400(string cursor) =>
        (await _party.GetAsync($"{Path}?after={Uri.EscapeDataString(cursor)}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Test]
    public async Task WithoutTheKey_Returns401_AndDuringAnOutage_503()
    {
        using var anonymous = _factory.PartyClient(apiKey: null);
        (await anonymous.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _party.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await _control.PostJsonAsync("/control/party/outage", """{ "on": false }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _control.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<JsonDocument> FeedAsync(string? after = null, int? limit = null)
    {
        var query = new List<string>();
        if (after is not null)
        {
            query.Add($"after={Uri.EscapeDataString(after)}");
        }

        if (limit is not null)
        {
            query.Add($"limit={limit}");
        }

        var response = await _party.GetAsync(query.Count == 0 ? Path : $"{Path}?{string.Join('&', query)}");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        return JsonDocument.Parse(content);
    }

    private static List<string> Numbers(JsonDocument page) =>
        page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("actionId").GetString()!.Split('-')[^1])
            .ToList();
}
