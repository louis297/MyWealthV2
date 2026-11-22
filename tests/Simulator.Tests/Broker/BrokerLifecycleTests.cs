using System.Net;
using System.Text.Json;
using MyWealthV2.Simulator.Tests.Control;
using MyWealthV2.Simulator.Tests.Hosting;
using MyWealthV2.Simulator.Tests.Market;

namespace MyWealthV2.Simulator.Tests.Broker;

/// <summary>Orders R58–R62, R64, R65: the operator's hand on a working order, the party cancel route, expiry,
/// settlement, the corporate action, the broker's own books and the reset.</summary>
public class BrokerLifecycleTests
{
    private const int Seed = 42;
    private const string OrderId = "2c0c8e1e-7c7c-4d55-9b53-0b8f5d3f6a11";
    private const string OtherOrderId = "7d1f3a55-0e0b-4c1e-8a57-5b7f0c2d9e42";

    // TestClock.Start is Thursday 2026-11-19.
    private static readonly DateOnly Day0 = DateOnly.FromDateTime(TestClock.Start.UtcDateTime);
    private static readonly DateOnly Monday = new(2026, 11, 23);

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

    /// <summary>A working order: the broker holds new orders.</summary>
    private async Task<string> WorkingAsync(string? body = null)
    {
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        return await BrokerHttp.PlaceOkAsync(_party, body ?? BrokerHttp.Order());
    }

    [Test]
    public async Task ControlFill_WithAQuantityAndAPrice_FillsThatPart_AndWithNeitherFillsTheRestAtTheBrokersPrice()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        var id = await WorkingAsync();

        var part = await BrokerHttp.FillAsync(_control, id, quantity: 4m, price: 101.5m);

        part.StatusCode.ShouldBe(HttpStatusCode.OK, await part.Content.ReadAsStringAsync());
        using (var json = await part.ReadJsonAsync())
        {
            var root = json.RootElement;
            root.EnumerateObject().Select(property => property.Name).ShouldBe(["order", "fills", "reports"]);
            var order = root.GetProperty("order");
            (order.GetProperty("brokerOrderId").GetString(), order.GetProperty("status").GetString(),
                    order.GetProperty("filledQuantity").GetDecimal())
                .ShouldBe((id, "Working", 4m));
            var fill = root.GetProperty("fills").EnumerateArray().Single();
            fill.EnumerateObject().Select(property => property.Name)
                .ShouldBe(["quantity", "price", "tradeDate", "settlementDate", "contractNote", "settled"]);
            (fill.GetProperty("quantity").GetDecimal(), fill.GetProperty("price").GetDecimal(), fill.GetProperty("tradeDate").GetString(),
                    fill.GetProperty("settlementDate").GetString(), fill.GetProperty("contractNote").GetString(),
                    fill.GetProperty("settled").GetBoolean())
                .ShouldBe((4m, 101.5m, "2026-11-19", "2026-11-23", "CN-1", false));
            var report = root.GetProperty("reports").EnumerateArray().Single();
            (report.GetProperty("id").GetString(), report.GetProperty("kind").GetString()).ShouldBe(($"{worldId:N}-X1", "Fill"));
        }

        var rest = await BrokerHttp.FillAsync(_control, id);

        rest.StatusCode.ShouldBe(HttpStatusCode.OK, await rest.Content.ReadAsStringAsync());
        var stored = (await BrokerHttp.OrdersAsync()).Single();
        (stored.Status, stored.FilledQuantity).ShouldBe((1, 10m));
        (await BrokerHttp.FillsAsync()).Select(fill => (fill.Number, fill.Quantity, fill.Price))
            .ShouldBe([(1, 4m, 101.5m), (2, 6m, BrokerHttp.FillPrice(100m, 0.10m, Seed, 2))]);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(0, 1, 2, null, null)]);
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerOrderModeChanged", "BrokerOrderFilled", "BrokerOrderFilled"]);
    }

    [TestCase(0, null, "Quantity")]
    [TestCase(10.5, null, "Quantity")]
    [TestCase(1.123456789, null, "Quantity")]
    [TestCase(null, 0, "Price")]
    [TestCase(null, 100.12345, "Price")]
    [TestCase(null, 105.01, "Price")]
    public async Task ControlFill_OutsideItsRules_Returns400AtTheField_AndFillsNothing(decimal? quantity, decimal? price, string field)
    {
        var id = await WorkingAsync();

        var response = await BrokerHttp.FillAsync(_control, id, quantity, price);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe([field]);
        (await BrokerHttp.FillsAsync()).ShouldBeEmpty();
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerOrderModeChanged"]);
    }

    [Test]
    public async Task ControlFill_ASellBelowItsLimit_AndTheBrokersPriceBeyondTheLimit_Return400AtPrice()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 200m, dailyMovePercent: 0m);
        var buy = await WorkingAsync(BrokerHttp.Order(symbol: "ACME.SIM"));
        var sell = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId, side: "Sell", priceLimit: 95m));

        var beyond = await BrokerHttp.FillAsync(_control, buy);
        var below = await BrokerHttp.FillAsync(_control, sell, price: 94.99m);

        (await beyond.ErrorKeysAsync()).ShouldBe(["Price"]);
        (await below.ErrorKeysAsync()).ShouldBe(["Price"]);
        (await BrokerHttp.FillsAsync()).ShouldBeEmpty();
        // Within the limits both fill.
        (await BrokerHttp.FillAsync(_control, buy, price: 105m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BrokerHttp.FillAsync(_control, sell, price: 95m)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ControlFillAndCancel_OfAnOrderThatIsNotWorking_Return400AtStatus_AndOfAnUnknownOrder404()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        var filled = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        var fill = await BrokerHttp.FillAsync(_control, filled);
        var cancel = await BrokerHttp.CancelAsync(_control, filled);

        fill.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fill.ErrorKeysAsync()).ShouldBe(["Status"]);
        cancel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await cancel.ErrorKeysAsync()).ShouldBe(["Status"]);
        foreach (var unknown in new[] { $"{worldId:N}-O9", $"{Guid.NewGuid():N}-O1", "nothing" })
        {
            (await BrokerHttp.FillAsync(_control, unknown)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await BrokerHttp.CancelAsync(_control, unknown)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await _control.GetAsync($"/control/broker/orders/{unknown}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await BrokerHttp.LogActionsAsync()).ShouldBeEmpty();
    }

    [TestCase(null, "Broker")]
    [TestCase("Trading halted", "Trading halted")]
    public async Task ControlCancel_CancelsAWorkingOrderAsTheBroker_WithACancelledReport_AndIsLogged(string? reason, string stored)
    {
        var id = await WorkingAsync();
        (await BrokerHttp.FillAsync(_control, id, quantity: 4m, price: 100m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await BrokerHttp.CancelAsync(_control, id, reason);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var json = await response.ReadJsonAsync())
        {
            var order = json.RootElement.GetProperty("order");
            (order.GetProperty("status").GetString(), order.GetProperty("filledQuantity").GetDecimal()).ShouldBe(("Cancelled", 4m));
            var last = json.RootElement.GetProperty("reports").EnumerateArray().Last();
            (last.GetProperty("kind").GetString(), last.GetProperty("reason").GetString()).ShouldBe(("Cancelled", stored));
        }

        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(1, 1, null, null, stored)]);
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerOrderModeChanged", "BrokerOrderFilled", "BrokerOrderCancelled"]);
    }

    [Test]
    public async Task ControlCancel_WithAReasonOver140Characters_OrEmpty_Returns400AtReason()
    {
        var id = await WorkingAsync();

        foreach (var reason in new[] { new string('r', 141), "" })
        {
            var response = await BrokerHttp.CancelAsync(_control, id, reason);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ErrorKeysAsync()).ShouldBe(["Reason"]);
        }

        (await BrokerHttp.OrdersAsync()).Single().Status.ShouldBe(0);
        (await BrokerHttp.CancelAsync(_control, id, new string('r', 140))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task PartyCancel_OfAWorkingOrder_IsAccepted_CancelsItAtOnce_WithAReportOfReasonRequested_AndIsNotLogged()
    {
        var id = await WorkingAsync();
        (await BrokerHttp.FillAsync(_control, id, quantity: 4m, price: 100m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await BrokerHttp.RequestCancelOkAsync(_party, OrderId);

        status.ShouldBe("Accepted");
        var order = (await BrokerHttp.OrdersAsync()).Single();
        (order.Status, order.FilledQuantity).ShouldBe((2, 4m));
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(1, 1, null, null, "Requested")]);
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerOrderModeChanged", "BrokerOrderFilled"]);
    }

    [Test]
    public async Task PartyCancel_OfAnOrderThatHasEnded_IsTooLate_AndOfAnUnknownClientOrderId404()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: "Refused");
        (await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order(OtherOrderId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = await WorkingAsync(BrokerHttp.Order("cancelled-once"));
        (await BrokerHttp.RequestCancelOkAsync(_party, "cancelled-once")).ShouldBe("Accepted");
        cancelled.ShouldNotBeNull();

        (await BrokerHttp.RequestCancelOkAsync(_party, OrderId)).ShouldBe("TooLate");
        (await BrokerHttp.RequestCancelOkAsync(_party, OtherOrderId)).ShouldBe("TooLate");
        (await BrokerHttp.RequestCancelOkAsync(_party, "cancelled-once")).ShouldBe("TooLate");
        (await BrokerHttp.RequestCancelAsync(_party, "never-sent")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await BrokerHttp.OrdersAsync()).Select(order => order.Status).ShouldBe([1, 4, 2]);
        (await BrokerHttp.ReportsAsync()).Count(report => report.Kind == 1).ShouldBe(1);
    }

    [Test]
    public async Task PartyCancel_WithoutTheKey_Returns401_AndDuringAnOutage503()
    {
        await WorkingAsync();
        using var noKey = _factory.PartyClient(apiKey: null);

        (await BrokerHttp.RequestCancelAsync(noKey, OrderId)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _control.PostJsonAsync("/control/party/outage", """{ "on": true }""")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BrokerHttp.RequestCancelAsync(_party, OrderId)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await BrokerHttp.OrdersAsync()).Single().Status.ShouldBe(0);
    }

    [Test]
    public async Task AdvancingTheClockADay_ExpiresTheOrdersStillWorking_EachWithAnExpiredReport()
    {
        await WorkingAsync();
        var second = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));
        (await BrokerHttp.FillAsync(_control, second, quantity: 3m, price: 100m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // The same day nothing expires.
        await BrokerHttp.SettingsAsync(_control);
        (await BrokerHttp.OrdersAsync()).Select(order => order.Status).ShouldBe([0, 0]);

        await MarketHttp.AdvanceAsync(_control, "P1D");
        // An order taken on the new day is working on it.
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("taken-today"));

        var orders = await BrokerHttp.OrdersAsync();
        orders.Select(order => (order.Status, order.FilledQuantity, order.AcceptedDay))
            .ShouldBe([(3, 0m, (DateOnly?)Day0), (3, 3m, (DateOnly?)Day0), (0, 0m, (DateOnly?)Day0.AddDays(1))]);
        (await BrokerHttp.ReportsAsync()).ShouldBe([
            new ReportRow(0, 2, 1, null, null), new ReportRow(2, 1, null, null, null), new ReportRow(2, 2, null, null, null)
        ]);
        // Nothing expires twice.
        await BrokerHttp.SettingsAsync(_control);
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(3);
        (await BrokerHttp.RequestCancelOkAsync(_party, OrderId)).ShouldBe("TooLate");
    }

    [Test]
    public async Task AdvancingToTheSettlementDate_SettlesTheFill_WithOneSettledReport_AndNotBefore()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        (await BrokerHttp.FillsAsync()).Single().SettlementDate.ShouldBe(Monday);

        // Sunday: the day before the settlement date.
        await MarketHttp.AdvanceAsync(_control, "P3D");
        await BrokerHttp.SettingsAsync(_control);
        (await BrokerHttp.FillsAsync()).Single().Settled.ShouldBeFalse();
        (await BrokerHttp.ReportsAsync()).Count.ShouldBe(1);

        await MarketHttp.AdvanceAsync(_control, "P1D");
        await BrokerHttp.SettingsAsync(_control);
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.FillsAsync()).Single().Settled.ShouldBeTrue();
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(3, 1, 1, Monday, null)]);
    }

    [Test]
    public async Task AClockThatJumpsPastSeveralSettlementDates_SettlesEachFillOnItsOwnDate_InDateOrder()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.SetSettlementDaysOkAsync(_control, 1);
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));

        await MarketHttp.AdvanceAsync(_control, "P10D");
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.ReportsAsync()).Skip(2).ShouldBe([
            new ReportRow(3, 2, 2, new DateOnly(2026, 11, 20), null), new ReportRow(3, 1, 1, Monday, null)
        ]);
    }

    [Test]
    public async Task SettlementDays0_SettlesTheFillOnItsTradeDate_AtTheNextRequest()
    {
        await BrokerHttp.SetSettlementDaysOkAsync(_control, 0);

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        (await BrokerHttp.FillsAsync()).Single().SettlementDate.ShouldBe(Day0);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null)]);
        await BrokerHttp.SettingsAsync(_control);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(0, 1, 1, null, null), new ReportRow(3, 1, 1, Day0, null)]);
    }

    // Thursday 19, Friday 20, Saturday 21, Sunday 22.
    [TestCase(0, 1, "2026-11-20")]
    [TestCase(0, 2, "2026-11-23")]
    [TestCase(0, 5, "2026-11-26")]
    [TestCase(1, 1, "2026-11-23")]
    [TestCase(1, 2, "2026-11-24")]
    [TestCase(2, 0, "2026-11-21")]
    [TestCase(2, 1, "2026-11-23")]
    [TestCase(3, 2, "2026-11-24")]
    public async Task TheSettlementDate_IsTheTradeDatePlusSettlementDaysWeekdays(int daysFromThursday, int settlementDays, string expected)
    {
        await BrokerHttp.SetSettlementDaysOkAsync(_control, settlementDays);
        if (daysFromThursday > 0)
        {
            await MarketHttp.AdvanceAsync(_control, $"P{daysFromThursday}D");
        }

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        var fill = (await BrokerHttp.FillsAsync()).Single();
        (fill.TradeDate, fill.SettlementDate).ShouldBe((Day0.AddDays(daysFromThursday), DateOnly.Parse(expected)));
    }

    [Test]
    public async Task SetSettlementDays_AppliesToLaterFills_AnswersTheSettings_AndIsLogged()
    {
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());

        var response = await BrokerHttp.SetSettlementDaysAsync(_control, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var json = await response.ReadJsonAsync())
        {
            json.RootElement.GetProperty("settlementDays").GetInt32().ShouldBe(1);
        }

        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OtherOrderId));
        (await BrokerHttp.FillsAsync()).Select(fill => fill.SettlementDate).ShouldBe([Monday, new DateOnly(2026, 11, 20)]);
        (await BrokerHttp.LogActionsAsync()).ShouldBe(["BrokerSettlementDaysSet"]);
    }

    [TestCase(null)]
    [TestCase(-1)]
    [TestCase(6)]
    public async Task SetSettlementDays_OutsideItsRange_Returns400(int? settlementDays)
    {
        var response = await BrokerHttp.SetSettlementDaysAsync(_control, settlementDays);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorKeysAsync()).ShouldBe(["SettlementDays"]);
        (await BrokerHttp.LogActionsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ACorporateActionThatBecomesEffective_CancelsTheWorkingOrdersInItsSymbol_BeforeTheOthersExpire()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
        await MarketHttp.AddOkAsync(_control, "BETA.SIM", "USD", 100m, dailyMovePercent: 0m);
        await WorkingAsync(BrokerHttp.Order("other-symbol", symbol: "BETA.SIM"));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order(OrderId, symbol: "acme.sim"));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("real-symbol"));
        await MarketHttp.AnnounceOkAsync(_control, "ACME.SIM", exDate: Day0.AddDays(2));

        await MarketHttp.AdvanceAsync(_control, "P2D");
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.OrdersAsync()).Select(order => (order.ClientOrderId, order.Status))
            .ShouldBe([("other-symbol", 3), (OrderId, 2), ("real-symbol", 3)]);
        (await BrokerHttp.ReportsAsync()).ShouldBe([
            new ReportRow(1, 2, null, null, "CorporateAction"), new ReportRow(2, 1, null, null, null), new ReportRow(2, 3, null, null, null)
        ]);
    }

    [Test]
    public async Task ACorporateActionThatWasWithdrawn_OrIsNotYetEffective_CancelsNothing_AndAnOrderTakenAfterTheExDateStays()
    {
        await MarketHttp.AddOkAsync(_control, "ACME.SIM", "USD", 100m, dailyMovePercent: 0m);
        await MarketHttp.AddOkAsync(_control, "BETA.SIM", "USD", 100m, dailyMovePercent: 0m);
        await MarketHttp.AddOkAsync(_control, "CORE.SIM", "USD", 100m, dailyMovePercent: 0m);
        await WorkingAsync(BrokerHttp.Order("withdrawn", symbol: "ACME.SIM"));
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("not-yet", symbol: "BETA.SIM"));
        var withdrawn = await MarketHttp.AnnounceOkAsync(_control, "ACME.SIM", exDate: Day0.AddDays(1));
        (await _control.PostJsonAsync($"/control/market/corporate-actions/{withdrawn}/cancel", "{}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MarketHttp.AnnounceOkAsync(_control, "BETA.SIM", exDate: Day0.AddDays(5));
        await MarketHttp.AnnounceOkAsync(_control, "CORE.SIM", exDate: Day0.AddDays(1));

        await MarketHttp.AdvanceAsync(_control, "P1D");
        // Taken on the ex-date, after the action became effective: on the new share basis.
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("after", symbol: "CORE.SIM"));
        await BrokerHttp.SettingsAsync(_control);

        (await BrokerHttp.OrdersAsync()).Select(order => (order.ClientOrderId, order.Status))
            .ShouldBe([("withdrawn", 3), ("not-yet", 3), ("after", 0)]);
        (await BrokerHttp.ReportsAsync()).ShouldBe([new ReportRow(2, 1, null, null, null), new ReportRow(2, 2, null, null, null)]);
    }

    [Test]
    public async Task TheBrokersBooks_ShowTheSettings_TheOrdersNewestFirstWithFilters_AndOneOrderWithItsFillsAndReports()
    {
        var worldId = await MarketHttp.WorldIdAsync();
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: "MarketClosed");
        (await BrokerHttp.PlaceAsync(_party, BrokerHttp.Order(OtherOrderId, symbol: "VOO", side: "Sell", priceLimit: 95m)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await BrokerHttp.SetModeOkAsync(_control, "Hold");
        await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order("third", symbol: "VOO"));

        var settings = await BrokerHttp.SettingsAsync(_control);
        settings.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["mode", "fillPercent", "rejectReason", "slippagePercent", "settlementDays"]);
        (settings.GetProperty("mode").GetString(), settings.GetProperty("fillPercent").GetInt32(),
                settings.GetProperty("rejectReason").ValueKind, settings.GetProperty("slippagePercent").GetDecimal(),
                settings.GetProperty("settlementDays").GetInt32())
            .ShouldBe(("Hold", 100, JsonValueKind.Null, 0.10m, 2));

        var page = await BrokerHttp.GetAsync(_control, "/control/broker/orders");
        page.EnumerateObject().Select(property => property.Name).ShouldBe(["items", "page", "pageSize", "totalCount"]);
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("totalCount").GetInt32())
            .ShouldBe((1, 20, 3));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("brokerOrderId").GetString())
            .ShouldBe([$"{worldId:N}-O3", $"{worldId:N}-O2", $"{worldId:N}-O1"]);
        items.Select(item => item.GetProperty("status").GetString()).ShouldBe(["Working", "Rejected", "Filled"]);
        var rejected = items[1];
        rejected.EnumerateObject().Select(property => property.Name).ShouldBe([
            "brokerOrderId", "clientOrderId", "symbol", "currency", "side", "quantity", "priceLimit", "referencePrice", "status",
            "rejectReason", "filledQuantity", "acceptedDay"
        ]);
        (rejected.GetProperty("clientOrderId").GetString(), rejected.GetProperty("symbol").GetString(),
                rejected.GetProperty("currency").GetString(), rejected.GetProperty("side").GetString(),
                rejected.GetProperty("quantity").GetDecimal(), rejected.GetProperty("priceLimit").GetDecimal(),
                rejected.GetProperty("referencePrice").GetDecimal(), rejected.GetProperty("rejectReason").GetString(),
                rejected.GetProperty("filledQuantity").GetDecimal(), rejected.GetProperty("acceptedDay").ValueKind)
            .ShouldBe((OtherOrderId, "VOO", "USD", "Sell", 10m, 95m, 100m, "MarketClosed", 0m, JsonValueKind.Null));
        items[2].GetProperty("acceptedDay").GetString().ShouldBe("2026-11-19");

        async Task<List<string?>> IdsAsync(string query) =>
            (await BrokerHttp.GetAsync(_control, "/control/broker/orders" + query)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("clientOrderId").GetString()).ToList();

        (await IdsAsync("?status=Filled")).ShouldBe([OrderId]);
        (await IdsAsync("?symbol=voo")).ShouldBe(["third", OtherOrderId]);
        (await IdsAsync("?symbol=VOO&status=Working")).ShouldBe(["third"]);
        (await IdsAsync("?page=2&pageSize=2")).ShouldBe([OrderId]);
        var unknown = await _control.GetAsync("/control/broker/orders?status=Open");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.ErrorKeysAsync()).ShouldBe(["Status"]);

        var one = await BrokerHttp.GetAsync(_control, $"/control/broker/orders/{worldId:N}-O1");
        one.GetProperty("order").GetProperty("clientOrderId").GetString().ShouldBe(OrderId);
        one.GetProperty("fills").EnumerateArray().Single().GetProperty("contractNote").GetString().ShouldBe("CN-1");
        var report = one.GetProperty("reports").EnumerateArray().Single();
        report.EnumerateObject().Select(property => property.Name).ShouldBe(["id", "kind", "settledDate", "reason"]);
        (report.GetProperty("id").GetString(), report.GetProperty("kind").GetString()).ShouldBe(($"{worldId:N}-X1", "Fill"));
    }

    [Test]
    public async Task AReset_EmptiesTheBooks_RestoresTheDefaults_AndTheIdsOfTheNewWorldDoNotRepeatTheOld()
    {
        var oldWorld = await MarketHttp.WorldIdAsync();
        var first = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        await BrokerHttp.SetModeOkAsync(_control, "Reject", reason: "Refused");
        await BrokerHttp.SetSlippageOkAsync(_control, 2.5m);
        await BrokerHttp.SetSettlementDaysOkAsync(_control, 5);
        first.ShouldBe($"{oldWorld:N}-O1");

        (await _control.PostJsonAsync("/control/reset", """{ "seed": 7 }""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var newWorld = await MarketHttp.WorldIdAsync();
        newWorld.ShouldNotBe(oldWorld);
        (await BrokerHttp.OrdersAsync()).ShouldBeEmpty();
        (await BrokerHttp.FillsAsync()).ShouldBeEmpty();
        (await BrokerHttp.ReportsAsync()).ShouldBeEmpty();
        var settings = await BrokerHttp.SettingsAsync(_control);
        (settings.GetProperty("mode").GetString(), settings.GetProperty("fillPercent").GetInt32(),
                settings.GetProperty("rejectReason").ValueKind, settings.GetProperty("slippagePercent").GetDecimal(),
                settings.GetProperty("settlementDays").GetInt32())
            .ShouldBe(("Fill", 100, JsonValueKind.Null, 0.10m, 2));

        // R65: an order of the old world is unknown to the broker.
        (await BrokerHttp.RequestCancelAsync(_party, OrderId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _control.GetAsync($"/control/broker/orders/{first}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var again = await BrokerHttp.PlaceOkAsync(_party, BrokerHttp.Order());
        again.ShouldBe($"{newWorld:N}-O1");
        var fill = (await BrokerHttp.FillsAsync()).Single();
        (fill.Number, fill.ContractNote, fill.Price).ShouldBe((1, "CN-1", BrokerHttp.FillPrice(100m, 0.10m, 7, 1)));
        var one = await BrokerHttp.GetAsync(_control, $"/control/broker/orders/{again}");
        one.GetProperty("reports").EnumerateArray().Single().GetProperty("id").GetString().ShouldBe($"{newWorld:N}-X1");
    }

    [Test]
    public async Task TheControlRoutes_NeedAnOperator_AndAreNotOnThePartyPort()
    {
        var id = await WorkingAsync();
        using var anonymous = _factory.ControlClient();

        (await anonymous.GetAsync("/control/broker")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await anonymous.GetAsync("/control/broker/orders")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await BrokerHttp.FillAsync(anonymous, id)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await BrokerHttp.CancelAsync(anonymous, id)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await BrokerHttp.SetSettlementDaysAsync(anonymous, 1)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await _party.GetAsync("/control/broker")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BrokerHttp.FillAsync(_party, id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await BrokerHttp.OrdersAsync()).Single().Status.ShouldBe(0);
    }
}
