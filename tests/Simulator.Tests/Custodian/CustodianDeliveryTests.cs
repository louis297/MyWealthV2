using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Broker;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Custodian;

/// <summary>In-kind transfers R67, R68: the broker delivers settled fills through the custodian, and a corporate action
/// changes the custodian's positions after the open fills in its symbol settle.</summary>
public class CustodianDeliveryTests
{
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, CustodianHttp.ResetCustodian);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BrokerHttp.ResetBroker);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, Bank.BankHttp.ResetBank);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
        await CustodianHttp.OpenOkAsync(_control);
    }

    [Test]
    public async Task ASettledBuyFill_OfAnOrderWithASafekeepingAccount_IsDeliveredIn_NamingItsFillReport()
    {
        await BrokerHttp.PlaceOkAsync(_party, Order("Buy", 10m, CustodianHttp.Account));

        await MarketHttp.AdvanceAsync(_control, "P4D");

        var movement = (await CustodianHttp.FeedAsync(_party)).Items.ShouldHaveSingleItem();
        (movement.GetProperty("kind").GetString(), movement.GetProperty("direction").GetString()).ShouldBe(("Trade", "In"));
        (movement.GetProperty("symbol").GetString(), movement.GetProperty("quantity").GetDecimal()).ShouldBe(("ACME.SIM", 10m));
        movement.GetProperty("tradeReference").GetString().ShouldBe(await FillReportIdAsync());
        movement.GetProperty("counterpartyName").GetString().ShouldBe("Simulator Broker");
        movement.GetProperty("settlementDate").GetString().ShouldBe("2026-11-23");
    }

    [Test]
    public async Task ASettledSellFill_IsDeliveredOut()
    {
        await BrokerHttp.PlaceOkAsync(_party, Order("Sell", 4m, CustodianHttp.Account));

        await MarketHttp.AdvanceAsync(_control, "P4D");

        (await CustodianHttp.FeedAsync(_party)).Items.Single().GetProperty("direction").GetString().ShouldBe("Out");
    }

    [TestCase(null)]
    [TestCase("NOT-AT-THE-CUSTODIAN")]
    public async Task AFillWithNoSafekeepingAccount_OrOneTheCustodianDoesNotHold_IsNotDelivered(string? account)
    {
        await BrokerHttp.PlaceOkAsync(_party, Order("Buy", 10m, account));

        await MarketHttp.AdvanceAsync(_control, "P4D");

        (await CustodianHttp.FeedAsync(_party)).Items.ShouldBeEmpty();
        (await BrokerHttp.FeedAsync(_party)).Items.ShouldContain(item => item.GetProperty("kind").GetString() == "Settled");
    }

    [Test]
    public async Task TheSafekeepingAccount_IsPartOfTheIdempotencyCheck_OnlyWhenSent()
    {
        await BrokerHttp.PlaceOkAsync(_party, Order("Buy", 1m, CustodianHttp.Account));

        (await BrokerHttp.PlaceAsync(_party, Order("Buy", 1m, CustodianHttp.Account))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BrokerHttp.PlaceAsync(_party, Order("Buy", 1m, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task ACorporateAction_AddsOneMovementPerAccountWithAPosition_ForTheDifference()
    {
        await AddPositionAsync(100m);
        await CustodianHttp.OpenOkAsync(_control, "SIM-EMPTY-USD");
        var actionId = await MarketHttp.AnnounceOkAsync(_control, "ACME.SIM", exDate: Day0.AddDays(2));

        await MarketHttp.AdvanceAsync(_control, "P2D");

        var movement = (await CustodianHttp.FeedAsync(_party)).Items.Single(item => item.GetProperty("kind").GetString() == "CorporateAction");
        (movement.GetProperty("direction").GetString(), movement.GetProperty("quantity").GetDecimal()).ShouldBe(("In", 100m));
        movement.GetProperty("corporateActionId").GetString().ShouldBe(actionId);
        movement.GetProperty("settlementDate").GetString().ShouldBe(Day0.AddDays(2).ToString("yyyy-MM-dd"));
        (await CustodianHttp.PositionsAsync(_control))["ACME.SIM"].ShouldBe(200m);
        (await CustodianHttp.FeedAsync(_party, "SIM-EMPTY-USD")).Items.ShouldBeEmpty();
    }

    [TestCase("Fractional", 1.5)]
    [TestCase("WholeShares", 1)]
    public async Task AConsolidation_RoundsTheNewPositionTowardsZero_ByTheRoundingMode(string mode, decimal expected)
    {
        await AddPositionAsync(3m);
        (await CustodianHttp.SetRoundingModeAsync(_control, mode)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MarketHttp.AnnounceOkAsync(_control, "ACME.SIM", "Consolidation", ratioNew: 1, ratioHeld: 2, exDate: Day0.AddDays(2));

        await MarketHttp.AdvanceAsync(_control, "P2D");

        (await CustodianHttp.PositionsAsync(_control))["ACME.SIM"].ShouldBe(expected);
        var movement = (await CustodianHttp.FeedAsync(_party)).Items.Single(item => item.GetProperty("kind").GetString() == "CorporateAction");
        (movement.GetProperty("direction").GetString(), movement.GetProperty("quantity").GetDecimal()).ShouldBe(("Out", 3m - expected));
    }

    [Test]
    public async Task OpenFillsInTheSymbol_SettleOnTheEffectiveDay_BeforeTheCustodianAppliesTheAction()
    {
        await BrokerHttp.SetSettlementDaysOkAsync(_control, 5);
        await BrokerHttp.PlaceOkAsync(_party, Order("Buy", 10m, CustodianHttp.Account));
        await MarketHttp.AnnounceOkAsync(_control, "ACME.SIM", exDate: Day0.AddDays(2));

        await MarketHttp.AdvanceAsync(_control, "P2D");

        var items = (await CustodianHttp.FeedAsync(_party)).Items;
        items.Select(item => item.GetProperty("kind").GetString()).ShouldBe(["Trade", "CorporateAction"]);
        items[0].GetProperty("settlementDate").GetString().ShouldBe(Day0.AddDays(2).ToString("yyyy-MM-dd"));
        items[1].GetProperty("quantity").GetDecimal().ShouldBe(10m);
        var settled = (await BrokerHttp.FeedAsync(_party)).Items.Single(item => item.GetProperty("kind").GetString() == "Settled");
        settled.GetProperty("settledDate").GetString().ShouldBe(Day0.AddDays(2).ToString("yyyy-MM-dd"));
    }

    private static string Order(string side, decimal quantity, string? safekeepingAccount)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(BrokerHttp.Order(symbol: "ACME.SIM", side: side, quantity: quantity,
            priceLimit: side == "Sell" ? 95m : 105m))!;
        if (safekeepingAccount is not null)
        {
            body["safekeepingAccount"] = safekeepingAccount;
        }

        return JsonSerializer.Serialize(body);
    }

    private async Task AddPositionAsync(decimal quantity) =>
        (await CustodianHttp.AddMovementAsync(_control, new { accountNumber = CustodianHttp.Account, direction = "In", symbol = "ACME.SIM", quantity,
            omitFromFeed = true })).StatusCode.ShouldBe(HttpStatusCode.Created);

    private async Task<string> FillReportIdAsync() =>
        (await BrokerHttp.FeedAsync(_party)).Items.Single(item => item.GetProperty("kind").GetString() == "Fill").GetProperty("id").GetString()!;
}
