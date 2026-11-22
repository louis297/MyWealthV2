using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Orders R52–R57: the simulator broker's order route, the order mode, the fill price and the fill.</summary>
public class BrokerOrderTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";
    private const string OtherOrderId = "7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42";

    // TestClock.Start is Thursday 2026-11-19.
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
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, BrokerHttp.ResetBroker);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, MarketHttp.ResetMarket);
        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, $"UPDATE [World] SET [Seed] = {Seed};");
    }

    [Test]
    public async Task Script0010_IsApplied_WithTheDefaultSettings()
    {
        var rows = await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT COUNT(*) FROM [SchemaVersions] WHERE [ScriptName] = N'0010_broker.sql';");
        ((int)rows.Single()[0]).ShouldBe(1);

        var settings = (await Sql.ReadAsync(SimulatorTestSetup.ConnectionString,
            "SELECT [Mode], [FillPercent], [RejectReason], [SlippagePercent], [SettlementDays] FROM [BrokerSettings]")).Single();
        ((int)settings[0], (int)settings[1], settings[2] is DBNull, (decimal)settings[3], (int)settings[4])
            .ShouldBe((0, 100, true, 0.10m, 2));
    }

    [Test]
    public async Task AnOrder_InModeFill_IsAcceptedAndFilledAtOnce_WithOneFillReport()
    {
        var worldId = await MarketHttp.WorldIdAsync();

        var response = await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order());

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using (var json = JsonDocument.Parse(content))
        {
            json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["brokerOrderId", "status"]);
            json.RootElement.GetProperty("brokerOrderId").GetString().ShouldBe($"{worldId:N}-O1");
            json.RootElement.GetProperty("status").GetString().ShouldBe("Accepted");
        }

        var order = (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem();
        order.ShouldBe(new OrderRow(1, OrderId, 1, 10m, Day0, null, "VTI", "USD", 0, 10m, 105m, 100m));
        var fill = (await BrokerHttp.FillsAsync()).ShouldHaveSingleItem();
        // R57, R61: traded on the simulator day, Thursday; settled two weekdays later, Monday.
        fill.ShouldBe(new FillRow(
            1, 1, 10m, BrokerHttp.FillPrice(100m, 0.10m, Seed, 1), Day0, new DateOnly(2026, 11, 23), "CN-1", false));
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null)]);
    }

    [Test]
    public async Task ASecondOrder_TakesTheNextNumbers()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        var second = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId, side: "Sell", priceLimit: 95m));

        second.ShouldBe($"{worldId:N}-O2");
        var fills = await BrokerHttp.FillsAsync();
        fills.Select(fill => (fill.Number, fill.OrderNumber, fill.ContractNote)).ShouldBe([(1, 1, "CN-1"), (2, 2, "CN-2")]);
        fills[1].Price.ShouldBe(BrokerHttp.FillPrice(100m, 0.10m, Seed, 2));
        (await BrokerHttp.OrdersAsync())[1].Side.ShouldBe(1);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(0, 2, 2, null, null)]);
    }

    [TestCase(10, 40, 4)]
    [TestCase(1.00000001, 33, 0.33)]
    [TestCase(0.00000001, 50, 0)]
    public async Task FillPercent_FillsThatPart_RoundedDown_AndLeavesTheOrderWorking(decimal quantity, int fillPercent, decimal filled)
    {
        await BrokerHttp.SetModeOkAsync(_control, "Fill", fillPercent);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(quantity: quantity));

        var order = (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem();
        (order.Status, order.FilledQuantity, order.Quantity).ShouldBe((0, filled, quantity));
        (await BrokerHttp.FillsAsync()).Select(fill => fill.Quantity).ShouldBe(filled > 0 ? [filled] : []);
    }

    [Test]
    public async Task ModeHold_AcceptsTheOrder_AndLeavesItWorking()
    {
        await BrokerHttp.SetModeOkAsync(_control, "Hold");

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        var order = (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem();
        (order.Status, order.FilledQuantity, order.AcceptedDay).ShouldBe((0, 0m, (DateOnly?)Day0));
        (await BrokerHttp.FillsAsync()).ShouldBeEmpty();
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
    }

    [TestCase("UnknownInstrument", 0)]
    [TestCase("InvalidOrder", 1)]
    [TestCase("MarketClosed", 2)]
    [TestCase("Refused", 3)]
    public async Task ModeReject_RejectsTheOrder_WithTheReason(string reason, int stored)
    {
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: reason);

        var response = await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order());

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using (var json = JsonDocument.Parse(content))
        {
            json.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["status", "reason"]);
            json.RootElement.GetProperty("status").GetString().ShouldBe("Rejected");
            json.RootElement.GetProperty("reason").GetString().ShouldBe(reason);
        }

        var order = (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem();
        (order.Status, order.RejectReason, order.AcceptedDay, order.FilledQuantity).ShouldBe((4, (int?)stored, (DateOnly?)null, 0m));
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task SetOrderMode_AnswersTheSettings_AndIsLogged_AndBackToFillClearsTheReason()
    {
        var reject = await BrokerHttp.SetModeAsync(_control, "Reject", reason: "MarketClosed");

        reject.StatusCode.ShouldBe(HttpStatusCode.OK, await reject.Content.ReadAsStringAsync());
        using (var json = await reject.ReadJsonAsync())
        {
            var root = json.RootElement;
            root.EnumerateObject().Select(property => property.Name)
                .ShouldBe(["mode", "fillPercent", "rejectReason", "slippagePercent", "settlementDays"]);
            (root.GetProperty("mode").GetString(), root.GetProperty("fillPercent").GetInt32(), root.GetProperty("rejectReason").GetString(),
                    root.GetProperty("slippagePercent").GetDecimal(), root.GetProperty("settlementDays").GetInt32())
                .ShouldBe(("Reject", 100, "MarketClosed", 0.10m, 2));
        }

        var fill = await BrokerHttp.SetModeAsync(_control, "Fill", fillPercent: 40, reason: "Refused");

        using (var json = await fill.ReadJsonAsync())
        {
            var root = json.RootElement;
            (root.GetProperty("mode").GetString(), root.GetProperty("fillPercent").GetInt32(), root.GetProperty("rejectReason").ValueKind)
                .ShouldBe(("Fill", 40, JsonValueKind.Null));
        }

        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerOrderModeChanged", "BrokerOrderModeChanged"]);
    }

    [TestCase(null, null, null, "Mode")]
    [TestCase("fill", null, null, "Mode")]
    [TestCase("Maybe", null, null, "Mode")]
    [TestCase("1", null, null, "Mode")]
    [TestCase("Fill", 0, null, "FillPercent")]
    [TestCase("Fill", 101, null, "FillPercent")]
    [TestCase("Reject", null, null, "Reason")]
    [TestCase("Reject", null, "Broken", "Reason")]
    [TestCase("Reject", null, "refused", "Reason")]
    public async Task SetOrderMode_OutsideItsRules_Returns400AtTheField_AndChangesNothing(
        string? mode, int? fillPercent, string? reason, string field)
    {
        var response = await BrokerHttp.SetModeAsync(_control, mode, fillPercent, reason);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe([field]);
        (await BrokerHttp.LogActionsAsync()).ShouldBeEmpty();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        (await BrokerHttp.OrdersAsync()).Single().Status.ShouldBe(1);
    }

    [Test]
    public async Task TheFillPrice_OfASimulatedInstrument_FollowsTheMarketsPrice_AndOfAnotherSymbolTheReferencePrice()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 50m, dailyMovePercent: 0m);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(symbol: "ACME.SIM"));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));

        var fills = await BrokerHttp.FillsAsync();
        fills.Select(fill => fill.Price).ShouldBe([BrokerHttp.FillPrice(50m, 0.10m, Seed, 1), BrokerHttp.FillPrice(100m, 0.10m, Seed, 2)]);
        // R56: within the slippage of 0.10 %.
        fills[0].Price.ShouldBeInRange(49.95m, 50.05m);
        fills[1].Price.ShouldBeInRange(99.90m, 100.10m);
    }

    [Test]
    public async Task TheFillPrice_OfASimulatedInstrument_IsTheMarketsPriceOfTheCurrentSimulatorDay()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 50m, dailyMovePercent: 10m);
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);
        await MarketHttp.AdvanceAsync(_control, "P3D");

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(symbol: "ACME.SIM", priceLimit: 1000m));

        var today = (await MarketHttp.PricesAsync(_control, "ACME.SIM"))[0];
        today.Date.ShouldBe(Day0.AddDays(3));
        var fill = (await BrokerHttp.FillsAsync()).ShouldHaveSingleItem();
        (fill.Price, fill.TradeDate).ShouldBe((today.Price, Day0.AddDays(3)));
    }

    [Test]
    public async Task TheFillPrice_IsTheSameForTheSameSeed_AndAnotherForAnotherSeed()
    {
        await BrokerHttp.SetSlippageOkAsync(_control, 5m);
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(priceLimit: 200m));
        var first = (await BrokerHttp.FillsAsync()).Single().Price;

        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString, "DELETE FROM [BrokerReports]; DELETE FROM [BrokerFills]; DELETE FROM [BrokerOrders];");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(priceLimit: 200m));
        var again = (await BrokerHttp.FillsAsync()).Single().Price;

        await Sql.ExecuteAsync(SimulatorTestSetup.ConnectionString,
            "DELETE FROM [BrokerReports]; DELETE FROM [BrokerFills]; DELETE FROM [BrokerOrders]; UPDATE [World] SET [Seed] = 43;");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(priceLimit: 200m));
        var other = (await BrokerHttp.FillsAsync()).Single().Price;

        first.ShouldBe(BrokerHttp.FillPrice(100m, 5m, Seed, 1));
        again.ShouldBe(first);
        other.ShouldBe(BrokerHttp.FillPrice(100m, 5m, 43, 1));
        other.ShouldNotBe(first);
        first.ShouldBeInRange(95m, 105m);
    }

    [Test]
    public async Task WithSlippage0_TheFillPriceIsTheBasePriceExactly_RoundedTo4Decimals()
    {
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(referencePrice: 100.123456m, priceLimit: 105.129629m));

        (await BrokerHttp.FillsAsync()).Single().Price.ShouldBe(100.1235m);
    }

    [Test]
    public async Task SetSlippage_AnswersTheSettings_AndIsLogged()
    {
        var response = await BrokerHttp.SetSlippageAsync(_control, 1.25m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = await response.ReadJsonAsync();
        json.RootElement.GetProperty("slippagePercent").GetDecimal().ShouldBe(1.25m);
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerSlippageSet"]);
    }

    [TestCase(null)]
    [TestCase(-0.01)]
    [TestCase(5.01)]
    [TestCase(0.125)]
    public async Task SetSlippage_OutsideItsRange_Returns400(decimal? slippagePercent)
    {
        var response = await BrokerHttp.SetSlippageAsync(_control, slippagePercent);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe(["SlippagePercent"]);
        (await BrokerHttp.LogActionsAsync()).ShouldBeEmpty();
    }

    [TestCase("Buy", 200, 105)]
    [TestCase("Sell", 200, 300)]
    public async Task APriceBeyondTheLimit_FillsNothing_AndTheOrderStaysWorking(string side, decimal marketPrice, decimal priceLimit)
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", marketPrice, dailyMovePercent: 0m);
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(symbol: "ACME.SIM", side: side, priceLimit: priceLimit));

        var order = (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem();
        (order.Status, order.FilledQuantity).ShouldBe((0, 0m));
        (await BrokerHttp.FillsAsync()).ShouldBeEmpty();
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
    }

    [TestCase("Buy", 105)]
    [TestCase("Sell", 105)]
    public async Task APriceAtTheLimit_Fills(string side, decimal priceLimit)
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 105m, dailyMovePercent: 0m);
        await BrokerHttp.SetSlippageOkAsync(_control, 0m);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(symbol: "ACME.SIM", side: side, priceLimit: priceLimit));

        (await BrokerHttp.FillsAsync()).Single().Price.ShouldBe(105m);
    }

    [Test]
    public async Task TheSameClientOrderIdAndBody_Again_GetsTheFirstAnswer_AndNothingNew()
    {
        var first = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: "Refused");

        var again = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        // The same order with its numbers written at another scale.
        var rescaled = await BrokerHttp.PlaceOkAsync(_party, $$"""
            { "clientOrderId": "{{OrderId}}", "symbol": "VTI", "currency": "USD", "side": "Buy", "quantity": 10.00000000,
              "type": "Market", "timeInForce": "Day", "priceLimit": 105.0, "referencePrice": 100.00 }
            """);

        (again, rescaled).ShouldBe((first, first));
        (await BrokerHttp.OrdersAsync()).Count.ShouldBe(1);
        (await BrokerHttp.FillsAsync()).Count.ShouldBe(1);
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task ARejectedOrder_Again_IsRejectedWithTheSameReason_WhateverTheModeNow()
    {
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: "MarketClosed");
        (await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order())).StatusCode.ShouldBe(HttpStatusCode.OK);
        await BrokerHttp.SetModeOkAsync(_control, "Fill");

        var again = await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order());

        using var json = await again.ReadJsonAsync();
        (json.RootElement.GetProperty("status").GetString(), json.RootElement.GetProperty("reason").GetString())
            .ShouldBe(("Rejected", "MarketClosed"));
        (await BrokerHttp.OrdersAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task TheSameClientOrderId_WithAnotherBody_Returns409_AndChangesNothing()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        foreach (var body in new[]
                 {
                     BrokerHttp.Order(quantity: 11m), BrokerHttp.Order(side: "Sell"), BrokerHttp.Order(symbol: "VOO"),
                     BrokerHttp.Order(priceLimit: 106m), BrokerHttp.Order(referencePrice: 101m), BrokerHttp.Order(currency: "NZD")
                 })
        {
            (await BrokerHttp.PlaceAsync(_party, body)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem().Quantity.ShouldBe(10m);
        (await BrokerHttp.FillsAsync()).Count.ShouldBe(1);
    }

    [TestCase("Fill")]
    [TestCase("Hold")]
    public async Task ASimulatedInstrument_InAnotherCurrencyThanItsOwn_IsRejectedAsUnknownInstrument_WhateverTheMode(string mode)
    {
        await MarketHttp.AddOkAsync(_control, "KIWI.SIM", "NZD", 5m);
        await BrokerHttp.SetModeOkAsync(_control, mode);

        var response = await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order(symbol: "kiwi.sim", currency: "USD"));

        using (var json = await response.ReadJsonAsync())
        {
            (json.RootElement.GetProperty("status").GetString(), json.RootElement.GetProperty("reason").GetString())
                .ShouldBe(("Rejected", "UnknownInstrument"));
        }

        (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem().Status.ShouldBe(4);
        // In its own currency the same instrument is taken.
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId, symbol: "KIWI.SIM", currency: "NZD", priceLimit: 5.25m, referencePrice: 5m));
    }

    [TestCase("clientOrderId", "\"\"")]
    [TestCase("clientOrderId", "null")]
    [TestCase("symbol", "\"\"")]
    [TestCase("symbol", "\"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456\"")]
    [TestCase("currency", "\"usd\"")]
    [TestCase("currency", "\"USDX\"")]
    [TestCase("side", "\"Hold\"")]
    [TestCase("side", "\"buy\"")]
    [TestCase("quantity", "0")]
    [TestCase("quantity", "-1")]
    [TestCase("quantity", "1.123456789")]
    [TestCase("quantity", "null")]
    [TestCase("priceLimit", "0")]
    [TestCase("priceLimit", "1.123456789")]
    [TestCase("referencePrice", "0")]
    [TestCase("referencePrice", "null")]
    [TestCase("type", "\"Limit\"")]
    [TestCase("timeInForce", "\"GoodTillCancelled\"")]
    public async Task ABodyOutsideTheFieldRules_Returns400_AndStoresNothing(string field, string value)
    {
        using var good = JsonDocument.Parse(BrokerHttp.Order());
        var body = "{" + string.Join(",", good.RootElement.EnumerateObject()
            .Select(property => $"\"{property.Name}\":{(property.Name == field ? value : property.Value.GetRawText())}")) + "}";

        var response = await BrokerHttp.PlaceAsync(_party, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await BrokerHttp.OrdersAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task AClientOrderIdOf100Characters_AndASymbolOf32_AreTaken()
    {
        var id = new string('a', 100);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(id, symbol: "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345"));

        (await BrokerHttp.OrdersAsync()).ShouldHaveSingleItem().ClientOrderId.ShouldBe(id);
    }

    [Test]
    public async Task WithoutTheKey_Returns401_AndDuringAnOutage503_AndNothingIsStored()
    {
        using var noKey = _factory.PartyClient(apiKey: null);
        using var wrongKey = _factory.PartyClient("wrong-party-api-key-0123456789abcdef");

        (await BrokerHttp.PlaceAsync(noKey, BrokerHttp.Order())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await BrokerHttp.PlaceAsync(wrongKey, BrokerHttp.Order())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order())).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await BrokerHttp.OrdersAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task TheOrderRoute_IsNotOnTheControlPort_AndTheControlRoutesNeedAnOperator()
    {
        using var anonymous = _factory.ControlClient();

        (await BrokerHttp.PlaceAsync(_control, BrokerHttp.Order())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BrokerHttp.SetModeAsync(anonymous, "Hold")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await BrokerHttp.SetModeAsync(_party, "Hold")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BrokerHttp.OrdersAsync()).ShouldBeEmpty();
    }
}
