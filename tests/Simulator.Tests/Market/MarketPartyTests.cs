using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Simulated instruments R21: <c>GET /party/market/prices</c>, the price route the platform reads.</summary>
public class MarketPartyTests
{
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
    }

    [Test]
    public async Task Prices_ListEveryInstrumentBySymbol_WithTheCurrentSimulatorDaysPrice()
    {
        await MarketHttp.AddOkAsync(_control, "KIWI.SIM", "NZD", 5m);
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m);
        await MarketHttp.AdvanceAsync(_control, "P2D");
        var acme = (await MarketHttp.PricesAsync(_control, "ACME.SIM"))[0];

        var response = await _party.GetAsync("/party/market/prices");

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(["asOf", "items"]);
        root.GetProperty("asOf").GetDateTimeOffset().ShouldBe(TestClock.Start.AddDays(2));
        var items = root.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("symbol").GetString()).ShouldBe(["ACME.SIM", "KIWI.SIM"]);
        items[0].EnumerateObject().Select(property => property.Name).ShouldBe(["symbol", "currency", "price", "date"]);
        items[0].GetProperty("currency").GetString().ShouldBe("USD");
        items[0].GetProperty("price").ValueKind.ShouldBe(JsonValueKind.Number);
        items[0].GetProperty("price").GetDecimal().ShouldBe(acme.Price);
        items[0].GetProperty("date").GetString().ShouldBe("2026-11-21");
        items[1].GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task Prices_BringTheMarketUpToDate_BeforeTheyAnswer()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");
        await MarketHttp.AdvanceAsync(_control, "P3D");

        (await _party.GetAsync("/party/market/prices")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [MarketPrices]");
        ((int)rows.Single()[0]).ShouldBe(4);
    }

    [Test]
    public async Task WithoutTheKey_Returns401_AndDuringAnOutage_503()
    {
        using var anonymous = _factory.PartyClient(apiKey: null);
        (await anonymous.GetAsync("/party/market/prices")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _party.GetAsync("/party/market/prices")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await _control.PostJsonAsync("/control/party/outage", """{ "on": false }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _party.GetAsync("/party/market/prices")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task TheControlPort_DoesNotServeThePrices()
    {
        (await _control.GetAsync("/party/market/prices")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ThePartyPort_DoesNotServeTheControlRoutes()
    {
        (await _party.GetAsync("/control/market/instruments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
