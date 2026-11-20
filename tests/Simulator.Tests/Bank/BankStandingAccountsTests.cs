using System.Net;
using MyWealthV2.Simulator.Tests.Control;

namespace MyWealthV2.Simulator.Tests.Bank;

/// <summary>Deposit matching R39, R47: standing accounts from configuration.</summary>
public class BankStandingAccountsTests
{
    private static readonly Dictionary<string, string?> Standing = new()
    {
        ["Simulator:Bank:StandingAccounts:0:AccountNumber"] = "SIM-DEMO-USD",
        ["Simulator:Bank:StandingAccounts:0:Currency"] = "USD",
        ["Simulator:Bank:StandingAccounts:0:Name"] = "Demo Firm Client Funds Trust Account",
        ["Simulator:Bank:StandingAccounts:1:AccountNumber"] = "SIM-DEMO-NZD",
        ["Simulator:Bank:StandingAccounts:1:Currency"] = "NZD",
        ["Simulator:Bank:StandingAccounts:1:Name"] = "Demo Firm Client Funds Trust Account"
    };

    [Test]
    public async Task FirstStart_OpensThem_ASecondStartOpensNothingNew_AndAResetOpensThemAgain()
    {
        var connectionString = await SimulatorTestSetup.CreateScratchDatabaseAsync("standing");

        using (var host = new SimulatorFactory(connectionString, settings: Standing))
        using (var control = host.SessionClient())
        {
            (await NumbersAsync(control)).ShouldBe(["SIM-DEMO-NZD", "SIM-DEMO-USD"]);
            await BankHttp.OpenOkAsync(control, "SIM-OTHER");
        }

        using (var host = new SimulatorFactory(connectionString, settings: Standing))
        using (var control = host.SessionClient())
        {
            (await NumbersAsync(control)).ShouldBe(["SIM-DEMO-NZD", "SIM-DEMO-USD", "SIM-OTHER"]);

            (await control.PostJsonAsync("/control/reset", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);

            (await NumbersAsync(control)).ShouldBe(["SIM-DEMO-NZD", "SIM-DEMO-USD"]);
        }
    }

    [TestCase("AccountNumber", "")]
    [TestCase("AccountNumber", "02/0100")]
    [TestCase("Currency", "usd")]
    [TestCase("Currency", "")]
    [TestCase("Name", "")]
    public void ABadEntry_StopsTheHostBeforeItListens(string field, string value)
    {
        var settings = new Dictionary<string, string?>(Standing)
        {
            [$"Simulator:Bank:StandingAccounts:0:{field}"] = value
        };

        using var host = new SimulatorFactory(settings: settings);

        Should.Throw<Exception>(() => host.Server).ToString().ShouldContain("Simulator:Bank:StandingAccounts");
    }

    [Test]
    public void TheSameNumberTwice_StopsTheHost()
    {
        var settings = new Dictionary<string, string?>(Standing)
        {
            ["Simulator:Bank:StandingAccounts:1:AccountNumber"] = "sim-demo-usd"
        };

        using var host = new SimulatorFactory(settings: settings);

        Should.Throw<Exception>(() => host.Server).ToString().ShouldContain("Simulator:Bank:StandingAccounts");
    }

    private static async Task<List<string>> NumbersAsync(HttpClient control) =>
        (await BankHttp.AccountsAsync(control)).GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("accountNumber").GetString()!)
        .Order(StringComparer.Ordinal)
        .ToList();
}
