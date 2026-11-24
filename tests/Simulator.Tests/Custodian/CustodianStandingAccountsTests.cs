using System.Net;
using MyWealthV2.Simulator.Tests.Hosting;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>In-kind transfers R61, R71: standing custodian accounts with their positions, and the reset.</summary>
public class CustodianStandingAccountsTests
{
    private SimulatorFactory _factory = null!;
    private HttpClient _control = null!;
    private HttpClient _party = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new SimulatorFactory(settings: new Dictionary<string, string?>
        {
            ["Simulator:Custodian:StandingAccounts:0:AccountNumber"] = "SIM-DEMO-CUSTODY-USD",
            ["Simulator:Custodian:StandingAccounts:0:Currency"] = "USD",
            ["Simulator:Custodian:StandingAccounts:0:Name"] = "Demo Advisory Nominees",
            ["Simulator:Custodian:StandingAccounts:0:Positions:0:Symbol"] = "VTI",
            ["Simulator:Custodian:StandingAccounts:0:Positions:0:Quantity"] = "1200"
        });
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

    [Test]
    public async Task AReset_OpensTheStandingAccountWithItsPosition_LeftOutOfTheFeed_AndSetsTheModesBack()
    {
        (await CustodianHttp.SetTransferModeAsync(_control, "Hold")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = "SIM-DEMO-CUSTODY-USD", direction = "In", symbol = "BND", quantity = 3m }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await _control.PostAsync("/control/reset", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (items, _, _, positions) = await CustodianHttp.FeedAsync(_party, "SIM-DEMO-CUSTODY-USD");
        items.ShouldBeEmpty();
        positions.Select(position => (position.GetProperty("symbol").GetString(), position.GetProperty("quantity").GetDecimal()))
            .ShouldBe([("VTI", 1200m)]);
        (await CustodianHttp.GetAsync(_control, "/control/custodian")).GetProperty("transferMode").GetString().ShouldBe("Execute");
    }
}
