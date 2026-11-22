using System.Net;
using MyWealthV2.Simulator.Tests.Control;

namespace MyWealthV2.Simulator.Tests.Market;

/// <summary>Simulated instruments R20, R23: standing instruments from configuration.</summary>
public class MarketStandingInstrumentsTests
{
    private static readonly Dictionary<string, string?> Standing = new()
    {
        ["Simulator:Market:StandingInstruments:0:Symbol"] = "ACME.SIM",
        ["Simulator:Market:StandingInstruments:0:Name"] = "Acme Simulated Holdings",
        ["Simulator:Market:StandingInstruments:0:Currency"] = "USD",
        ["Simulator:Market:StandingInstruments:0:Price"] = "100.00",
        ["Simulator:Market:StandingInstruments:1:Symbol"] = "KIWI.SIM",
        ["Simulator:Market:StandingInstruments:1:Name"] = "Kiwi Simulated Fund",
        ["Simulator:Market:StandingInstruments:1:Currency"] = "NZD",
        ["Simulator:Market:StandingInstruments:1:Price"] = "5.00",
        ["Simulator:Market:StandingInstruments:1:DailyMovePercent"] = "0.5"
    };

    [Test]
    public async Task FirstStart_AddsThem_ASecondStartLeavesThemAsTheyAre_AndAResetAddsThemAgain()
    {
        var connectionString = await SimulatorTestSetup.CreateScratchDatabaseAsync("market-standing");

        using (var host = new SimulatorFactory(connectionString, settings: Standing))
        using (var control = host.SessionClient())
        {
            var items = await ItemsAsync(control);
            items.ShouldBe([("ACME.SIM", "USD", 100m, 2m), ("KIWI.SIM", "NZD", 5m, 0.5m)]);
            (await MarketHttp.PricesAsync(control, "ACME.SIM")).ShouldHaveSingleItem().Set.ShouldBeFalse();
            (await MarketHttp.SetPriceAsync(control, "ACME.SIM", 120m)).StatusCode.ShouldBe(HttpStatusCode.OK);
            await MarketHttp.AddOkAsync(control, "OTHER.SIM");
        }

        using (var host = new SimulatorFactory(connectionString, settings: Standing))
        using (var control = host.SessionClient())
        {
            (await ItemsAsync(control)).ShouldBe([
                ("ACME.SIM", "USD", 120m, 2m), ("KIWI.SIM", "NZD", 5m, 0.5m), ("OTHER.SIM", "USD", 100m, 2m)
            ]);

            (await control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

            (await ItemsAsync(control)).ShouldBe([("ACME.SIM", "USD", 100m, 2m), ("KIWI.SIM", "NZD", 5m, 0.5m)]);
        }
    }

    [TestCase("Symbol", "ACME")]
    [TestCase("Symbol", "")]
    [TestCase("Name", "")]
    [TestCase("Currency", "usd")]
    [TestCase("Price", "0")]
    [TestCase("Price", "ten")]
    [TestCase("DailyMovePercent", "25")]
    public void ABadEntry_StopsTheHostBeforeItListens(string field, string value)
    {
        var settings = new Dictionary<string, string?>(Standing)
        {
            [$"Simulator:Market:StandingInstruments:0:{field}"] = value
        };

        using var host = new SimulatorFactory(settings: settings);

        Should.Throw<Exception>(() => host.Server).ToString().ShouldContain("Simulator:Market:StandingInstruments");
    }

    [Test]
    public void TheSameSymbolTwice_StopsTheHost()
    {
        var settings = new Dictionary<string, string?>(Standing)
        {
            ["Simulator:Market:StandingInstruments:1:Symbol"] = "acme.sim"
        };

        using var host = new SimulatorFactory(settings: settings);

        Should.Throw<Exception>(() => host.Server).ToString().ShouldContain("Simulator:Market:StandingInstruments");
    }

    private static async Task<List<(string, string, decimal, decimal)>> ItemsAsync(HttpClient control) =>
        (await MarketHttp.InstrumentsAsync(control)).GetProperty("items").EnumerateArray()
        .Select(item => (
            item.GetProperty("symbol").GetString()!,
            item.GetProperty("currency").GetString()!,
            item.GetProperty("price").GetDecimal(),
            item.GetProperty("dailyMovePercent").GetDecimal()))
        .ToList();
}
