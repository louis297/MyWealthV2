using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Simulated instruments R14–R19, R22–R24: the simulator market's instruments and their price path.</summary>
public class MarketControlTests
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
    }

    [Test]
    public async Task Script0008_IsApplied()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0008_market.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);
    }

    [Test]
    public async Task AddInstrument_Returns201_InUpperCase_WithTodaysPrice_AndLogsTheAction()
    {
        var response = await MarketHttp.AddAsync(_control, " acme.sim ", "USD", 100.00m, dailyMovePercent: 2.5m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location!.ToString().ShouldBe("/control/market/instruments/ACME.SIM");
        using var json = await response.ReadJsonAsync();
        var item = json.RootElement;
        item.GetProperty("symbol").GetString().ShouldBe("ACME.SIM");
        item.GetProperty("name").GetString().ShouldBe("Acme Simulated Holdings");
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("price").GetDecimal().ShouldBe(100m);
        item.GetProperty("priceDate").GetString().ShouldBe("2026-11-19");
        item.GetProperty("dailyMovePercent").GetDecimal().ShouldBe(2.5m);
        (await MarketHttp.PricesAsync(_control, "ACME.SIM")).ShouldBe([(Day0, 100m, true)]);
        (await LogActionsAsync()).ShouldBe(["MarketInstrumentAdded"]);
    }

    [Test]
    public async Task AddInstrument_WithoutADailyMove_MovesUpToTwoPercent()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        var item = (await MarketHttp.InstrumentsAsync(_control)).GetProperty("items").EnumerateArray().Single();
        item.GetProperty("dailyMovePercent").GetDecimal().ShouldBe(2m);
    }

    [TestCase("ACME")]
    [TestCase("ACME.US")]
    [TestCase(".SIM")]
    [TestCase("ACME SIM.SIM")]
    [TestCase("ACMEACMEACMEACMEACMEACMEACMEA.SIM")]
    [TestCase("")]
    public async Task AddInstrument_ABadSymbol_Returns400AtSymbol_AndLogsNothing(string symbol)
    {
        var response = await MarketHttp.AddAsync(_control, symbol);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["Symbol"]);
        (await LogActionsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AddInstrument_ASymbolThatExists_InAnyCase_Returns400AtSymbol()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        var response = await MarketHttp.AddAsync(_control, "acme.sim");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe(["Symbol"]);
        (await LogActionsAsync()).ShouldBe(["MarketInstrumentAdded"]);
    }

    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD", "price": 0 }""", "Price")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD", "price": 1.00001 }""", "Price")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD" }""", "Price")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD", "price": 1, "dailyMovePercent": 20.01 }""", "DailyMovePercent")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD", "price": 1, "dailyMovePercent": -1 }""", "DailyMovePercent")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "USD", "price": 1, "dailyMovePercent": 1.555 }""", "DailyMovePercent")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": "Acme", "currency": "usd", "price": 1 }""", "Currency")]
    [TestCase("""{ "symbol": "ACME.SIM", "name": " ", "currency": "USD", "price": 1 }""", "Name")]
    public async Task AddInstrument_ABadField_Returns400AtItsField(string body, string field)
    {
        var response = await _control.PostJsonAsync("/control/market/instruments", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorKeysAsync()).ShouldBe([field]);
        (await LogActionsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ThePriceOfTheNextDay_FollowsTheSeededStep()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", price: 100m, dailyMovePercent: 2m);
        await MarketHttp.AdvanceAsync(_control, "P1D");

        var prices = await MarketHttp.PricesAsync(_control, "ACME.SIM");

        var expected = Step(100m, 2m, Seed, "ACME.SIM", Day0.AddDays(1));
        prices.ShouldBe([(Day0.AddDays(1), expected, false), (Day0, 100m, true)]);
        Math.Abs(expected - 100m).ShouldBeLessThanOrEqualTo(2m);
        var item = (await MarketHttp.InstrumentsAsync(_control)).GetProperty("items")[0];
        item.GetProperty("price").GetDecimal().ShouldBe(expected);
        item.GetProperty("priceDate").GetString().ShouldBe("2026-11-20");
    }

    [Test]
    public async Task AnAdvanceOfThreeDays_StoresThreePricesOnTheNextRead_EachFromTheDayBefore()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", price: 50m, dailyMovePercent: 5m);
        await MarketHttp.AdvanceAsync(_control, "P3D");

        var prices = await MarketHttp.PricesAsync(_control, "ACME.SIM");

        prices.Count.ShouldBe(4);
        var day1 = Step(50m, 5m, Seed, "ACME.SIM", Day0.AddDays(1));
        var day2 = Step(day1, 5m, Seed, "ACME.SIM", Day0.AddDays(2));
        var day3 = Step(day2, 5m, Seed, "ACME.SIM", Day0.AddDays(3));
        prices.Select(price => price.Price).ShouldBe([day3, day2, day1, 50m]);
    }

    [Test]
    public async Task TheSameSeedAndSteps_GiveTheSamePath_AfterAReset()
    {
        async Task<List<(DateOnly, decimal, bool)>> RunAsync()
        {
            _factory.Clock.Set(TestClock.Start);
            (await _control.PostJsonAsync("/control/reset", $$"""{ "seed": {{Seed}} }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
            await MarketHttp.AddOkAsync(_control, "ACME.SIM", price: 10m, dailyMovePercent: 10m);
            await MarketHttp.AdvanceAsync(_control, "P2D");
            await MarketHttp.AdvanceAsync(_control, "PT30H");
            return await MarketHttp.PricesAsync(_control, "ACME.SIM");
        }

        var first = await RunAsync();
        var second = await RunAsync();

        second.ShouldBe(first);
        first.Count.ShouldBe(4);
    }

    [Test]
    public async Task ADailyMoveOfZero_KeepsThePrice()
    {
        await MarketHttp.AddOkAsync(_control, "FLAT.SIM", price: 7.5m, dailyMovePercent: 0m);
        await MarketHttp.AdvanceAsync(_control, "P3D");

        (await MarketHttp.PricesAsync(_control, "FLAT.SIM")).Select(price => price.Price).ShouldBe([7.5m, 7.5m, 7.5m, 7.5m]);
    }

    [Test]
    public async Task TwoReadsAtOnce_StoreEachDayOnce()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");
        await MarketHttp.AdvanceAsync(_control, "P5D");
        using var other = _factory.SessionClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            (i % 2 == 0 ? _control : other).GetAsync("/control/market/instruments")));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString, "SELECT COUNT(*) FROM [MarketPrices]");
        ((int)rows.Single()[0]).ShouldBe(6);
    }

    [Test]
    public async Task SetPrice_ReplacesTodaysPrice_TheNextDayStepsFromIt_AndItIsLogged()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", price: 100m, dailyMovePercent: 2m);
        await MarketHttp.AdvanceAsync(_control, "P1D");

        var response = await MarketHttp.SetPriceAsync(_control, "acme.sim", 80.1234m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var json = await response.ReadJsonAsync())
        {
            json.RootElement.GetProperty("price").GetDecimal().ShouldBe(80.1234m);
        }

        await MarketHttp.AdvanceAsync(_control, "P1D");
        var prices = await MarketHttp.PricesAsync(_control, "ACME.SIM");
        prices.ShouldBe([
            (Day0.AddDays(2), Step(80.1234m, 2m, Seed, "ACME.SIM", Day0.AddDays(2)), false),
            (Day0.AddDays(1), 80.1234m, true),
            (Day0, 100m, true)
        ]);
        (await LogActionsAsync()).ShouldBe(["ClockAdvanced", "MarketPriceSet", "ClockAdvanced", "MarketInstrumentAdded"]);
    }

    [Test]
    public async Task SetPrice_AnUnknownSymbol_Returns404_AndABadPrice_Returns400()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        (await MarketHttp.SetPriceAsync(_control, "NONE.SIM", 1m)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bad = await MarketHttp.SetPriceAsync(_control, "ACME.SIM", 0m);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ErrorKeysAsync()).ShouldBe(["Price"]);
    }

    [Test]
    public async Task SetDailyMove_AppliesFromTheNextDay_AndItIsLogged()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", price: 100m, dailyMovePercent: 0m);

        var response = await MarketHttp.SetDailyMoveAsync(_control, "ACME.SIM", 15m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var json = await response.ReadJsonAsync())
        {
            json.RootElement.GetProperty("dailyMovePercent").GetDecimal().ShouldBe(15m);
            json.RootElement.GetProperty("price").GetDecimal().ShouldBe(100m);
        }

        await MarketHttp.AdvanceAsync(_control, "P1D");
        (await MarketHttp.PricesAsync(_control, "ACME.SIM"))[0].Price
            .ShouldBe(Step(100m, 15m, Seed, "ACME.SIM", Day0.AddDays(1)));
        (await LogActionsAsync()).ShouldBe(["ClockAdvanced", "MarketDailyMoveSet", "MarketInstrumentAdded"]);
    }

    [Test]
    public async Task SetDailyMove_AnUnknownSymbol_Returns404_AndOutsideItsRange_Returns400()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        (await MarketHttp.SetDailyMoveAsync(_control, "NONE.SIM", 1m)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bad = await MarketHttp.SetDailyMoveAsync(_control, "ACME.SIM", 21m);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ErrorKeysAsync()).ShouldBe(["DailyMovePercent"]);
    }

    [Test]
    public async Task Prices_OfAnUnknownSymbol_Returns404()
    {
        (await _control.GetAsync("/control/market/instruments/NONE.SIM/prices")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Instruments_AreListedBySymbol()
    {
        await MarketHttp.AddOkAsync(_control, "ZED.SIM");
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        (await MarketHttp.InstrumentsAsync(_control)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("symbol").GetString())
            .ShouldBe(["ACME.SIM", "ZED.SIM"]);
    }

    [Test]
    public async Task AReset_EmptiesTheMarket()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM");

        (await _control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await MarketHttp.InstrumentsAsync(_control)).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await _control.GetAsync("/control/market/instruments/ACME.SIM/prices")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task WithoutASession_Returns401()
    {
        using var anonymous = _factory.ControlClient();

        (await anonymous.GetAsync("/control/market/instruments")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await MarketHttp.AddAsync(anonymous, "ACME.SIM")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AddInstrument_AsText_Returns415()
    {
        var response = await _control.PostAsync("/control/market/instruments",
            new StringContent("""{ "symbol": "ACME.SIM" }""", System.Text.Encoding.UTF8, "text/plain"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    /// <summary>R17, worked out here from the rule: <c>Max(0.0001, Round(previous × (1 + move), 4))</c> with
    /// <c>move = dailyMovePercent / 100 × (2u − 1)</c>, <c>u</c> the first draw of (seed, market-price:{symbol}, day
    /// number from 2000-01-01).</summary>
    private static decimal Step(decimal previous, decimal dailyMovePercent, int seed, string symbol, DateOnly day)
    {
        var n = day.DayNumber - new DateOnly(2000, 1, 1).DayNumber;
        var u = (decimal)SeededRandom.For(seed, $"market-price:{symbol}", n).NextDouble();
        var move = dailyMovePercent / 100m * (2m * u - 1m);
        return Math.Max(0.0001m, Math.Round(previous * (1m + move), 4, MidpointRounding.AwayFromZero));
    }

    private static async Task<List<string>> LogActionsAsync()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT [Action] FROM [ControlLog] ORDER BY [Id] DESC");
        return rows.Select(row => (string)row[0]).ToList();
    }
}
